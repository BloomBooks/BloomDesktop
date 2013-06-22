<?xml version="1.0" encoding="UTF-8"?>
<!--

Where I left this...

* was experimenting with <page>.... some docs seemed to sugget that InDesign would add pages, if... what? Maybe if there was a top level tag on the page somehow?

* experimenting with Table. THere are apparently 2 kinds CALS and something else. Which one should we write? Why?

* Newlines messed up

-->
<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">

	<!-- BOOK WRAPPER -->
	<xsl:template match="/">
		<Book>
			<xsl:apply-templates />
		</Book>
	</xsl:template>

	<xsl:template match="div[@id='bloomDataDiv']">
		<!-- skip the data div -->
	</xsl:template>

	<!--  PAGE WRAPPER -->

	<xsl:template match="*[@data-export and contains(@class,'bloom-page')]">
		<xsl:text>&#xa;</xsl:text>
		<xsl:element name="{@data-export}">
			<xsl:apply-templates />
			<xsl:text>&#xa;</xsl:text>
		</xsl:element>
		<xsl:text>&#xa;</xsl:text>
	</xsl:template>

	<xsl:template match="*[contains(@class,'bloom-page') and not(@data-export)]">
		<xsl:text>&#xa;</xsl:text>
			<page>
			<xsl:apply-templates />
				<xsl:text>&#xa;</xsl:text>
			</page>
		<xsl:text>&#xa;</xsl:text>
	</xsl:template>

	<!-- IMAGES   -->
	<xsl:template match="div[@data-export and contains(@class, 'bloom-imageContainer')]">
		<xsl:text>&#xa;</xsl:text>
		 <xsl:apply-templates />
	</xsl:template>

	<xsl:template match="img">
		<xsl:if test="@data-export | ../@data-export">
			<xsl:element name="{@data-export | ../@data-export}">
					<xsl:attribute name="href">
					<xsl:value-of select="@src"/>
					</xsl:attribute>
				 </xsl:element>
		</xsl:if>
		<xsl:if test="not(@data-export | ../@data-export)">
			<xsl:text>&#xa;</xsl:text>
			<image>
				<xsl:attribute name="href">
					<xsl:value-of select="@src"/>
				</xsl:attribute>
			</image>
		</xsl:if>
		<xsl:text>&#xa;</xsl:text>
	</xsl:template>

	<!-- Normal Text (TranslationGroups) -->
	<xsl:template match="*[@data-export and contains(@class,'bloom-translationGroup')]">
			  <xsl:apply-templates />
	</xsl:template>

	<xsl:template match="*[@data-export and normalize-space(text()) and not(@contenteditable='true')]">
		<xsl:element name="{@data-export}">
			<!--<xsl:value-of select="text()"/>-->
			<xsl:apply-templates/>
			<xsl:value-of select="normalize-space()" />
		</xsl:element>
		<xsl:text>&#xa;</xsl:text>
	</xsl:template>

	<!-- For multilingual templates add attributes which convey the language number (1,2, or 3) and language code.
		The number here would be 1 normally, but if it was exported in bilingual mode, then you'd get a 2 as well, etc.
		We wouldn't expect an InDesign template to match on language codes, but it's nice to not throw it away.    -->

	<!-- language 1 -->
<!--    <xsl:template match="*[@data-export and normalize-space(text()) and (@contenteditable='true') and contains(@class,'bloom-content1')]">-->
	<xsl:template match="*[(@contenteditable= 'true') and contains(@class,'bloom-content1')]">
		<xsl:text>&#xa;</xsl:text>
		<xsl:if test="not(../@data-export)">
			<xsl:element name="text">
				<xsl:value-of select="normalize-space()" />
			 </xsl:element>
		</xsl:if>
		<xsl:if test="@data-export | ../@data-export != ''">
			<xsl:element name="{@data-export | ../@data-export}">
				<xsl:attribute name="langNum">1</xsl:attribute>
				<xsl:attribute name="langCode">
					<xsl:value-of select="@lang"/>
				</xsl:attribute>
				<!--<xsl:value-of select="text()"/>-->
				<xsl:value-of select="normalize-space()" />
			</xsl:element>
		</xsl:if>
	</xsl:template>
	<!-- language 2 -->
	<xsl:template match="*[@data-export and normalize-space(text()) and (@contenteditable='true') and contains(@class,'bloom-content2')]">
		<xsl:text>&#xa;</xsl:text>
		<xsl:element name="{@data-export}">
			<xsl:attribute name="langNum">2</xsl:attribute>
			<xsl:attribute name="langCode">
				<xsl:value-of select="@lang"/>
			</xsl:attribute>
			<xsl:apply-templates />

			<!--<xsl:value-of select="text()"/>-->
			<xsl:value-of select="normalize-space()" />
		</xsl:element>
	</xsl:template>

	<!-- language 3 -->
	<xsl:template match="*[@data-export and normalize-space(text()) and (@contenteditable='true') and contains(@class,'bloom-content3')]">
		<xsl:element name="{@data-export}">
			<xsl:attribute name="langNum">3</xsl:attribute>
			<xsl:attribute name="langCode">
				<xsl:value-of select="@lang"/>
			</xsl:attribute>

			<xsl:apply-templates />

			<!--<xsl:value-of select="text()"/>-->
			<xsl:value-of select="normalize-space()" />

		</xsl:element>
		<xsl:text>&#xa;</xsl:text>
	</xsl:template>

	<xsl:template match="table">
		<table>
			<xsl:apply-templates select="*/tr/td"/>
		</table>
		<xsl:text>&#xa;</xsl:text>
	</xsl:template>

	<xsl:template match="td">
		<cell table="cell">
			<xsl:value-of select="text()"/>
		</cell>
		<xsl:text>&#xa;</xsl:text>
	</xsl:template>

	<xsl:template match="text()|@*">
	</xsl:template>
</xsl:stylesheet>
