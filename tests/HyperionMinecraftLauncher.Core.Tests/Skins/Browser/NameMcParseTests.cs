using System.Linq;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

// v0.32.3: NameMcSkinBrowser is [Obsolete] (Cloudflare killed pure-HTTP NameMC
// access), but the parser is still exercised here as a regression contract so a
// future provider with similar HTML can crib the selector strategy.
#pragma warning disable CS0618

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins.Browser;

/// <summary>
/// Synthetic-fixture tests for <see cref="NameMcSkinBrowser.ParseGallery"/>.
/// These never touch the live network: every test feeds a small HTML string
/// shaped like a NameMC gallery page and asserts the parser produces the
/// expected <see cref="BrowsedSkin"/> records.
/// </summary>
public class NameMcParseTests
{
    [Fact]
    public void ParseGallery_EmptyOrNullHtml_ReturnsEmpty()
    {
        Assert.Empty(NameMcSkinBrowser.ParseGallery(string.Empty, 10));
        Assert.Empty(NameMcSkinBrowser.ParseGallery(null!, 10));
    }

    [Fact]
    public void ParseGallery_HtmlWithNoCards_ReturnsEmpty()
    {
        const string Html = "<html><body><h1>nothing here</h1></body></html>";
        Assert.Empty(NameMcSkinBrowser.ParseGallery(Html, 10));
    }

    [Fact]
    public void ParseGallery_ExtractsHashAndBuildsCanonicalUrls()
    {
        const string Hash = "1234567890abcdef1234567890abcdef";
        var html = $@"
<html><body>
  <div class='card'>
    <a class='card-img-top' href='/skin/{Hash}' data-model='classic'>
      <img src='https://s.namemc.com/3d/skin/body.png?id={Hash}&amp;model=classic' />
    </a>
  </div>
</body></html>";

        var result = NameMcSkinBrowser.ParseGallery(html, 10);

        Assert.Single(result);
        var skin = result[0];
        Assert.Equal(Hash, skin.Id);
        Assert.Equal("https://namemc.com/skin/" + Hash, skin.SourceUrl);
        Assert.Equal(NameMcSkinBrowser.TextureCdn + Hash, skin.PngDownloadUrl);
        Assert.Equal(SkinVariant.Classic, skin.Variant);
        Assert.Contains(Hash, skin.ThumbnailUrl);
    }

    [Fact]
    public void ParseGallery_DetectsSlimVariant_FromDataModel()
    {
        const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var html = $@"
<html><body>
  <div class='card'>
    <a class='card-img-top' href='/skin/{Hash}' data-model='slim'>
      <img src='/img/skin-thumb.png' />
    </a>
  </div>
</body></html>";

        var result = NameMcSkinBrowser.ParseGallery(html, 10);

        Assert.Single(result);
        Assert.Equal(SkinVariant.Slim, result[0].Variant);
    }

    [Fact]
    public void ParseGallery_DetectsSlimVariant_FromImgSrcModelQuery()
    {
        const string Hash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var html = $@"
<html><body>
  <div class='card'>
    <a class='card-img-top' href='/skin/{Hash}'>
      <img src='https://s.namemc.com/3d/skin/body.png?id={Hash}&amp;model=slim' />
    </a>
  </div>
</body></html>";

        var result = NameMcSkinBrowser.ParseGallery(html, 10);

        Assert.Single(result);
        Assert.Equal(SkinVariant.Slim, result[0].Variant);
    }

    [Fact]
    public void ParseGallery_DedupesRepeatedCardsByHash()
    {
        const string Hash = "cccccccccccccccccccccccccccccccc";
        var html = $@"
<html><body>
  <div class='card'>
    <a class='card-img-top' href='/skin/{Hash}'><img src='/a.png' /></a>
  </div>
  <div class='card'>
    <a class='card-img-top' href='/skin/{Hash}'><img src='/b.png' /></a>
  </div>
</body></html>";

        var result = NameMcSkinBrowser.ParseGallery(html, 10);

        Assert.Single(result);
    }

    [Fact]
    public void ParseGallery_RespectsLimit()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<html><body>");
        for (var i = 0; i < 8; i++)
        {
            var hash = new string((char)('a' + i), 32);
            sb.Append($"<div class='card'><a href='/skin/{hash}'><img src='/{i}.png' /></a></div>");
        }
        sb.Append("</body></html>");

        var result = NameMcSkinBrowser.ParseGallery(sb.ToString(), 3);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseGallery_SkipsMalformedCardsAndKeepsRest()
    {
        const string Hash = "dddddddddddddddddddddddddddddddd";
        var html = $@"
<html><body>
  <a href='/not-a-skin/whatever'>nope</a>
  <a href='#'>also nope</a>
  <div class='card'>
    <a class='card-img-top' href='/skin/{Hash}'><img src='/c.png' /></a>
  </div>
</body></html>";

        var result = NameMcSkinBrowser.ParseGallery(html, 10);

        Assert.Single(result);
        Assert.Equal(Hash, result[0].Id);
    }

    [Fact]
    public void ParseGallery_PicksUpUploaderAndTagsAndLikes()
    {
        const string Hash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var html = $@"
<html><body>
  <div class='card'>
    <a class='card-img-top' href='/skin/{Hash}'><img src='/d.png' /></a>
    <div class='card-body'>
      <a href='/profile/Notch'>Notch</a>
      <a href='/tag/anime'>anime</a>
      <span class='tag'>boy</span>
      <span class='heart' data-count='1,234'>1234</span>
    </div>
  </div>
</body></html>";

        var result = NameMcSkinBrowser.ParseGallery(html, 10);

        Assert.Single(result);
        var skin = result[0];
        Assert.Equal("Notch", skin.UploaderName);
        Assert.Contains("anime", skin.Tags);
        Assert.Contains("boy", skin.Tags);
        Assert.Equal(1234, skin.Likes);
    }

    [Fact]
    public void ExtractHashFromHref_HandlesAbsoluteAndRelative()
    {
        Assert.Equal("abc", NameMcSkinBrowser.ExtractHashFromHref("/skin/abc"));
        Assert.Equal("abc", NameMcSkinBrowser.ExtractHashFromHref("https://namemc.com/skin/abc"));
        Assert.Equal("abc", NameMcSkinBrowser.ExtractHashFromHref("/skin/abc?foo=bar"));
        Assert.Equal("abc", NameMcSkinBrowser.ExtractHashFromHref("/skin/abc/"));
        Assert.Null(NameMcSkinBrowser.ExtractHashFromHref("/notskin/abc"));
        Assert.Null(NameMcSkinBrowser.ExtractHashFromHref(""));
    }
}
