using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Caching;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;
using System.Xml;
using System.Xml.XPath;
using EsccWebTeam.Data.Xml;
using Microsoft.ApplicationBlocks.ExceptionManagement;

namespace EsccWebTeam.Feeds
{
    /// <summary>
    /// Display a feed on a web page
    /// </summary>
    public class FeedControl : WebControl, INamingContainer
    {
        private Uri feedUri;
        private ITemplate itemTemplate;
        private ITemplate headerTemplate;
        private ITemplate footerTemplate;
        private int maxItems = -1;
        private int refreshInterval = 60;

        /// <summary>
        /// Initializes a new instance of the <see cref="FeedControl"/> class.
        /// </summary>
        public FeedControl()
            : base(HtmlTextWriterTag.Div)
        {
        }

        /// <summary>
        /// Gets or sets the maximum number of items to display.
        /// </summary>
        /// <value>The maximum number of items.</value>
        public int MaximumItems
        {
            get { return maxItems; }
            set { maxItems = value; }
        }

        /// <summary>
        /// Gets or sets the feed URI.
        /// </summary>
        /// <value>The feed URI.</value>
        /// <remarks>This is a string rather than a Uri type so that it can be used declaritively</remarks>
        public string FeedUri
        {
            get { return (this.feedUri != null) ? this.feedUri.ToString() : String.Empty; }
            set { feedUri = new Uri(value, UriKind.RelativeOrAbsolute); }
        }


        /// <summary>
        /// Gets or sets the frequency, in minutes, that the feed is updated from the remote site.
        /// </summary>
        /// <value>The refresh interval in minutes.</value>
        public int RefreshInterval
        {
            get { return this.refreshInterval; }
            set { this.refreshInterval = value; }
        }

        /// <summary>
        /// XHTML within which each feed item will be displayed.
        /// </summary>
        /// <remarks>
        /// <para>To insert the title of the feed item in your XHTML, use an &lt;asp:literal runat=&quot;server&quot; /&gt; with an id of &quot;title&quot;.</para>
        /// <para>To link to the feed item in your XHTML, use an ordinary link with an id of &quot;link&quot;. Remember to include the runat=&quot;server&quot; attribute.</para>
        /// </remarks>
        [TemplateContainer(typeof(XhtmlContainer))]
        public ITemplate ItemTemplate
        {
            get { return this.itemTemplate; }
            set { this.itemTemplate = value; }
        }

        /// <summary>
        /// XHTML to be displayed as a header if any items are in the feed
        /// </summary>
        [TemplateContainer(typeof(XhtmlContainer))]
        public ITemplate HeaderTemplate
        {
            get { return this.headerTemplate; }
            set { this.headerTemplate = value; }
        }

        /// <summary>
        /// XHTML to be displayed as a footer if any items are in the feed
        /// </summary>
        [TemplateContainer(typeof(XhtmlContainer))]
        public ITemplate FooterTemplate
        {
            get { return this.footerTemplate; }
            set { this.footerTemplate = value; }
        }

        /// <summary>
        /// Called by the ASP.NET page framework to notify server controls that use composition-based implementation to create any child controls they contain in preparation for posting back or rendering.
        /// </summary>
        protected override void CreateChildControls()
        {
            try
            {
                // If there's no feed URI, there's nothing to show
                if (feedUri == null) return;

                // If there's no item template, there's no way to show the data
                if (this.itemTemplate == null)
                {
                    throw new NullReferenceException("The ItemTemplate property must not be null");
                }

                // Try first to get the XML out of the cache
                XPathDocument feedXml = ReadXml();

                // Hopefully we've got some XML data, so now display it
                if (feedXml != null)
                {
                    DisplayFeed(feedXml);
                }
                else this.Visible = false;
            }
            catch (Exception ex)
            {
                // We don't want exceptions bringing down the whole page this is displayed on, so fail silently and publish the error
                this.Visible = false;

                // Don't want endless notifications of the same error if this control is on a popular page either
                string errorCachekey = "EsccWebTeam.Rss.LastError";
                if (this.Page.Cache[errorCachekey] != null) return;

                // Publish the error
                NameValueCollection additionalInfo = new NameValueCollection();
                additionalInfo.Add("Feed URI", this.feedUri.ToString());
                additionalInfo.Add("Next time you'll be notified if this error continues", DateTime.UtcNow.AddMinutes(10).ToShortTimeString());
                ExceptionManager.Publish(ex, additionalInfo);
                this.Page.Cache.Insert(errorCachekey, ex.Message, null, DateTime.UtcNow.AddMinutes(10), System.Web.Caching.Cache.NoSlidingExpiration);
            }
        }

        private void DisplayFeed(XPathDocument feedXml)
        {
            // Move to the root node of the document, from where we can launch an XPath query
            XPathNavigator nav = feedXml.CreateNavigator();
            nav.MoveToRoot();
            nav.MoveToFirstChild();

            // Get all the feed items
            XPathNodeIterator it = nav.Select("/rss/channel/item");

            if (it.Count > 0)
            {
                // If we've got some items to display, and we've got a header, display the header
                if (this.headerTemplate != null)
                {
                    XhtmlContainer headerControl = new XhtmlContainer();
                    headerTemplate.InstantiateIn(headerControl);
                    headerControl.ID = "header";
                    this.Controls.Add(headerControl);
                }

                // If we've got some items to display, iterate through them until we run out, or
                // until we reach the maximum number which can be set using the MaximumItems property
                while (it.MoveNext() && (this.maxItems <= 0 || it.CurrentPosition <= this.maxItems))
                {
                    // There may be circumstances where we want to filter the data, e.g. we may only be interested in items that are up to 2 months old.
                    // To do this we need a test point. By default the filter will just return a 'true' value, but it can be overridden where required.
                    if (this.ItemPassesFilterCheck(it))
                    {
                        // Create the item based on a template provided
                        XhtmlContainer itemControl = new XhtmlContainer();
                        itemTemplate.InstantiateIn(itemControl);
                        this.Controls.Add(itemControl);

                        // If the item template has a Literal with an id of "title", add the item title to it
                        Literal titleControl = itemControl.FindControl("title") as Literal;
                        if (titleControl != null)
                        {
                            XPathNavigator title = it.Current.SelectSingleNode("title");
                            // Set the title (using a method that can be overrided by inheriting controls)
                            this.SetItemTitle(titleControl, title);
                        }

                        // If the item template has a link with an id of "link", set the href of the link to the item's link
                        HtmlAnchor linkControl = itemControl.FindControl("link") as HtmlAnchor;
                        if (linkControl != null)
                        {
                            XPathNavigator link = it.Current.SelectSingleNode("link");
                            // Set the href of the link (using a method that can be overrided by inheriting controls)
                            this.SetItemLink(linkControl, link);
                        }

                        // If the item template has a Literal with an id of "pubDate", add the item publish date to it
                        Literal pubdateControl = itemControl.FindControl("pubDate") as Literal;
                        if (pubdateControl != null)
                        {
                            XPathNavigator pubDate = it.Current.SelectSingleNode("pubDate");
                            // Set the title (using a method that can be overrided by inheriting controls)
                            this.SetItemPublishDate(pubdateControl, pubDate);
                        }
                    }
                }

                // If we've got some items to display, and we've got a footer, display the footer
                if (this.footerTemplate != null)
                {
                    XhtmlContainer footerControl = new XhtmlContainer();
                    footerTemplate.InstantiateIn(footerControl);
                    footerControl.ID = "footer";
                    this.Controls.Add(footerControl);
                }

                this.Visible = true;
            }
            else this.Visible = false;
        }

        private XPathDocument ReadXml()
        {
            // Don't want endless notifications of the same error if this control is on a popular page,
            // nor to keep requesting the XML if the feed itself is the problem. If there's been an error 
            // in the last 10 minutes, don't try again.
            string errorCachekey = "EsccWebTeam.Feeds.LastError." + this.feedUri;
            if (this.Page.Cache[errorCachekey] != null) return null;

            // Variable to store the text of the response, so we can include it in the error message if it's not valid XML
            string responseString = null;

            // We don't want to make a web request every time the page is displayed, so cache the
            // feed in memory. Because it's a shared application cache, use a key which will be
            // unique to this assembly and to this feed URI. Regex strips anything non-alphanumeric from the feed URI.
            string cachekey = "EsccWebTeam.Rss." + Regex.Replace(this.feedUri.ToString(), "[^a-z0-9]*", String.Empty);

            try
            {
                XPathDocument feedXml = null;
                if (this.Page.Cache[cachekey] != null)
                {
                    feedXml = (XPathDocument)this.Page.Cache[cachekey];
                }
                else
                {
                    // If it's not in the cache, download the feed

                    // Create a web request
                    WebRequest xmlRequest = XmlHttpRequest.Create(this.feedUri);
                    xmlRequest.Timeout = 5000;

                    // Make the request and, assuming it returns a valid result, load it into an XPathDocument
                    // and cache that for use in future requests
                    HttpWebResponse xmlResponse = xmlRequest.GetResponse() as HttpWebResponse;
                    if (xmlResponse.StatusCode == HttpStatusCode.OK)
                    {
                        StreamReader sr = new StreamReader(xmlResponse.GetResponseStream());
                        responseString = sr.ReadToEnd(); // stash the response in a string so that, if it's not valid XML, we can include it in the error message.
                        feedXml = new XPathDocument(new StringReader(responseString));
                        this.Page.Cache.Insert(cachekey, feedXml, null, System.DateTime.UtcNow.AddMinutes(this.refreshInterval), System.Web.Caching.Cache.NoSlidingExpiration);
                    }

                    // Report old config settings as error
                    if (ConfigurationManager.GetSection("EsccWebTeam.Feeds/ConnectionSettings") != null)
                    {
                        ExceptionManager.Publish(new ConfigurationErrorsException("EsccWebTeam.Feeds/ConnectionSettings web.config section is obsolete. Use EsccWebTeam.Data.Xml/Proxy instead."));
                    }
                }
                return feedXml;
            }
            catch (WebException ex)
            {
                return ExceptionGettingFeed(errorCachekey, responseString, ex);
            }
            catch (XmlException ex)
            {
                return ExceptionGettingFeed(errorCachekey, responseString, ex);
            }
        }

        private XPathDocument ExceptionGettingFeed(string errorCachekey, string responseString, Exception ex)
        {
            // We don't want exceptions bringing down the whole page this is displayed on, so fail silently and publish the error
            this.Visible = false;

            // We don't want to know about the first exception, because intermittent exceptions are expected. 
            // We do want to know about repeated exceptions though. 
            string suppressFirstErrorCacheKey = "EsccWebTeam.Feeds.SuppressFirst." + this.feedUri;
            if (Page.Cache[suppressFirstErrorCacheKey] != null)
            {
                // Already suppressed one, so report the error
                var additionalInfo = new NameValueCollection();
                additionalInfo.Add("Feed URI", this.feedUri.ToString());
                additionalInfo.Add("Next time you'll be notified if this error continues", DateTime.UtcNow.AddMinutes(10).ToShortTimeString());
                if (!String.IsNullOrEmpty(responseString)) additionalInfo.Add("Response body", responseString);
                ExceptionManager.Publish(ex, additionalInfo);
            }
            else
            {
                // Set up cache key so that the error gets reported if it happens again. 
                // This time period should get reset every time the cache is checked by the if statement.
                Page.Cache.Insert(suppressFirstErrorCacheKey, true, null, Cache.NoAbsoluteExpiration, TimeSpan.FromMinutes(20));
            }

            // Don't check again for 10 minutes
            this.Page.Cache.Insert(errorCachekey, ex.Message, null, DateTime.UtcNow.AddMinutes(10), System.Web.Caching.Cache.NoSlidingExpiration);

            return null;
        }

        /// <summary>
        /// The point at which an item is checked to see if it is filtered out or not
        /// </summary>
        protected virtual bool ItemPassesFilterCheck(XPathNodeIterator it)
        {
            // By default every item passes the check.
            return true;
        }

        /// <summary>
        /// The point at which the title is set for a given feed item.
        /// </summary>
        /// <param name="titleControl"></param>
        /// <param name="xpathNav"></param>
        protected virtual void SetItemTitle(Literal titleControl, XPathNavigator xpathNav)
        {
            titleControl.Text = xpathNav.InnerXml;
        }

        /// <summary>
        /// The point at which the link is set for a given feed item.
        /// </summary>
        /// <param name="linkControl"></param>
        /// <param name="xpathNav"></param>
        protected virtual void SetItemLink(HtmlAnchor linkControl, XPathNavigator xpathNav)
        {
            linkControl.HRef = xpathNav.InnerXml;
        }

        /// <summary>
        /// The point at which the publish date is set for a given feed item.
        /// </summary>
        /// <param name="pubdateControl"></param>
        /// <param name="xpathNav"></param>
        protected virtual void SetItemPublishDate(Literal pubdateControl, XPathNavigator xpathNav)
        {
            pubdateControl.Text = xpathNav.InnerXml;
        }

        /// <summary>
        /// Gets a value indicating whether this instance has successfully read some data from a feed.
        /// </summary>
        /// <value><c>true</c> if this instance has data; otherwise, <c>false</c>.</value>
        public bool HasFeedData
        {
            get
            {
                this.EnsureChildControls();
                return this.Controls.Count > 0;
            }
        }
    }
}
