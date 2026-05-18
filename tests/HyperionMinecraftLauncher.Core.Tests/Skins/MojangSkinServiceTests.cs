using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins;

public class MojangSkinServiceTests
{
    [Fact]
    public async Task UploadSkin_NullToken_ThrowsLauncherException()
    {
        var service = new MojangSkinService(new HttpClient(new StubHandler()));
        var ex = await Assert.ThrowsAsync<SkinUploadFailedException>(
            () => service.UploadSkinAsync("", new byte[] { 0x89 }, SkinVariant.Classic, CancellationToken.None));
        Assert.Contains("Microsoft account", ex.Message);
    }

    [Fact]
    public async Task UploadSkin_Success_PostsMultipartWithVariantAndBearer()
    {
        var stub = new StubHandler { ResponseStatus = HttpStatusCode.OK };
        var service = new MojangSkinService(new HttpClient(stub));

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03 };
        await service.UploadSkinAsync("TOKEN-123", png, SkinVariant.Slim, CancellationToken.None);

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api.minecraftservices.com/minecraft/profile/skins", req.RequestUri!.ToString());
        Assert.NotNull(req.AuthHeader);
        Assert.Equal("Bearer", req.AuthHeader!.Scheme);
        Assert.Equal("TOKEN-123", req.AuthHeader.Parameter);

        // Body should be multipart with a "variant" field containing "slim".
        Assert.Contains("multipart/form-data", req.ContentType ?? string.Empty);
        // Content-Disposition may use single or double quotes depending on runtime; accept either.
        Assert.True(req.RawBody.Contains("name=\"variant\"") || req.RawBody.Contains("name=variant"),
            $"variant field not found in body: {req.RawBody}");
        Assert.Contains("slim", req.RawBody);
        Assert.True(req.RawBody.Contains("name=\"file\"") || req.RawBody.Contains("name=file"),
            $"file field not found in body: {req.RawBody}");
    }

    [Fact]
    public async Task UploadSkin_HttpFailure_SurfacesSkinUploadFailedException()
    {
        var stub = new StubHandler
        {
            ResponseStatus = HttpStatusCode.Forbidden,
            ResponseBody = "{\"errorMessage\":\"nope\"}",
        };
        var service = new MojangSkinService(new HttpClient(stub));

        var ex = await Assert.ThrowsAsync<SkinUploadFailedException>(
            () => service.UploadSkinAsync("t", new byte[] { 0x89, 0x01 }, SkinVariant.Classic, CancellationToken.None));
        Assert.Contains("403", ex.Message);
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public async Task UploadSkin_Classic_SendsClassicVariantLiteral()
    {
        var stub = new StubHandler { ResponseStatus = HttpStatusCode.OK };
        var service = new MojangSkinService(new HttpClient(stub));

        await service.UploadSkinAsync("t", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, SkinVariant.Classic, CancellationToken.None);

        var req = Assert.Single(stub.Requests);
        Assert.Contains("classic", req.RawBody);
        // Ensure the variant literal sent on the wire is "classic" not "slim".
        Assert.DoesNotContain(">slim<", req.RawBody);
        Assert.DoesNotContain("\nslim\r\n", req.RawBody);
    }

    [Fact]
    public async Task GetProfile_Success_ParsesSkinsAndCapes()
    {
        const string body = """
        {
          "id": "069a79f444e94726a5befca90e38aaf5",
          "name": "Notch",
          "skins": [
            { "id": "skin-a", "state": "ACTIVE",   "url": "https://textures.minecraft.net/texture/skinA", "variant": "CLASSIC", "alias": "default" },
            { "id": "skin-b", "state": "INACTIVE", "url": "https://textures.minecraft.net/texture/skinB", "variant": "SLIM" }
          ],
          "capes": [
            { "id": "cape-1", "state": "ACTIVE",   "url": "https://textures.minecraft.net/texture/c1", "alias": "Migrator" },
            { "id": "cape-2", "state": "INACTIVE", "url": "https://textures.minecraft.net/texture/c2", "alias": "Founder's" }
          ]
        }
        """;
        var stub = new StubHandler { ResponseStatus = HttpStatusCode.OK, ResponseBody = body };
        var service = new MojangSkinService(new HttpClient(stub));

        var profile = await service.GetProfileAsync("TKN", CancellationToken.None);

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("https://api.minecraftservices.com/minecraft/profile", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.AuthHeader!.Scheme);
        Assert.Equal("TKN", req.AuthHeader.Parameter);

        Assert.Equal("069a79f444e94726a5befca90e38aaf5", profile.Id);
        Assert.Equal("Notch", profile.Name);
        Assert.Equal(2, profile.Skins.Count);
        Assert.Equal("ACTIVE", profile.Skins[0].State);
        Assert.Equal("CLASSIC", profile.Skins[0].Variant);
        Assert.Equal("default", profile.Skins[0].Alias);
        Assert.Equal("INACTIVE", profile.Skins[1].State);
        Assert.Equal("SLIM", profile.Skins[1].Variant);
        Assert.Null(profile.Skins[1].Alias);

        Assert.Equal(2, profile.Capes.Count);
        Assert.Equal("cape-1", profile.Capes[0].Id);
        Assert.Equal("ACTIVE", profile.Capes[0].State);
        Assert.Equal("Migrator", profile.Capes[0].Alias);
    }

    [Fact]
    public void ParseProfile_NoSkinsOrCapes_ReturnsEmptyCollections()
    {
        const string body = """{ "id": "x", "name": "y" }""";
        var profile = MojangSkinService.ParseProfile(body);

        Assert.Empty(profile.Skins);
        Assert.Empty(profile.Capes);
        Assert.Equal("x", profile.Id);
        Assert.Equal("y", profile.Name);
    }

    [Fact]
    public async Task SetActiveCape_PutsJsonBodyWithCapeId()
    {
        var stub = new StubHandler { ResponseStatus = HttpStatusCode.OK };
        var service = new MojangSkinService(new HttpClient(stub));

        await service.SetActiveCapeAsync("TKN", "cape-1", CancellationToken.None);

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Equal("https://api.minecraftservices.com/minecraft/profile/capes/active", req.RequestUri!.ToString());
        Assert.StartsWith("application/json", req.ContentType ?? string.Empty);

        using var doc = JsonDocument.Parse(req.RawBody);
        Assert.Equal("cape-1", doc.RootElement.GetProperty("capeId").GetString());
    }

    [Fact]
    public async Task SetActiveCape_EmptyId_Throws()
    {
        var stub = new StubHandler { ResponseStatus = HttpStatusCode.OK };
        var service = new MojangSkinService(new HttpClient(stub));

        await Assert.ThrowsAsync<SkinUploadFailedException>(
            () => service.SetActiveCapeAsync("TKN", "", CancellationToken.None));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task ClearActiveCape_SendsDelete()
    {
        var stub = new StubHandler { ResponseStatus = HttpStatusCode.OK };
        var service = new MojangSkinService(new HttpClient(stub));

        await service.ClearActiveCapeAsync("TKN", CancellationToken.None);

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Delete, req.Method);
        Assert.Equal("https://api.minecraftservices.com/minecraft/profile/capes/active", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.AuthHeader!.Scheme);
    }

    [Fact]
    public async Task GetProfile_NullToken_ThrowsBeforeNetwork()
    {
        var stub = new StubHandler();
        var service = new MojangSkinService(new HttpClient(stub));

        await Assert.ThrowsAsync<SkinUploadFailedException>(
            () => service.GetProfileAsync(null!, CancellationToken.None));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task ClearActiveCape_NullToken_ThrowsBeforeNetwork()
    {
        var stub = new StubHandler();
        var service = new MojangSkinService(new HttpClient(stub));

        await Assert.ThrowsAsync<SkinUploadFailedException>(
            () => service.ClearActiveCapeAsync("", CancellationToken.None));
        Assert.Empty(stub.Requests);
    }

    private sealed class CapturedRequest
    {
        public HttpMethod Method { get; init; } = HttpMethod.Get;
        public Uri? RequestUri { get; init; }
        public System.Net.Http.Headers.AuthenticationHeaderValue? AuthHeader { get; init; }
        public string? ContentType { get; init; }
        public string RawBody { get; init; } = string.Empty;
    }

    /// <summary>
    /// Snapshots each request (method, URL, auth header, body) so the assertions can
    /// inspect them without depending on the real network. Returns a configurable response.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "{}";
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = string.Empty;
            string? contentType = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                contentType = request.Content.Headers.ContentType?.ToString();
            }

            Requests.Add(new CapturedRequest
            {
                Method = request.Method,
                RequestUri = request.RequestUri,
                AuthHeader = request.Headers.Authorization,
                ContentType = contentType,
                RawBody = body,
            });

            var resp = new HttpResponseMessage(ResponseStatus)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
            return resp;
        }
    }
}
