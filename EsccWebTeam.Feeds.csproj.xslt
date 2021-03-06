﻿<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:msxsl="urn:schemas-microsoft-com:xslt" exclude-result-prefixes="msxsl"
    xmlns="http://schemas.microsoft.com/developer/msbuild/2003"
    xmlns:msbuild="http://schemas.microsoft.com/developer/msbuild/2003"
>
  <xsl:output method="xml" indent="yes"/>

  <!-- TransformProjectFile.xslt is from Escc.AzureDeployment and will be copied into this folder at deploy time  -->
  <xsl:include href="TransformProjectFile.xslt"/>

  <xsl:template match="msbuild:Project/msbuild:ItemGroup/msbuild:Reference/msbuild:HintPath">
    <xsl:call-template name="UpdateHintPath">
      <xsl:with-param name="ref_1" select="'Microsoft.ApplicationBlocks.ExceptionManagement.dll'" />
    </xsl:call-template>

  </xsl:template>

</xsl:stylesheet>
