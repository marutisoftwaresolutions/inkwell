using System.Net;
using System.Text.RegularExpressions;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Cropping an image saves a copy and leaves the original alone; replacing a file keeps its address
/// and refuses a different kind of file. The crop arithmetic is pure and never leaves the picture.
/// </summary>
public class MediaCropReplaceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public MediaCropReplaceTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public void A_crop_is_kept_inside_the_picture()
    {
        Assert.Equal(new ImageCrop.Rect(10, 20, 100, 50), ImageCrop.Clamp(800, 600, 10, 20, 100, 50));
        Assert.Equal(new ImageCrop.Rect(700, 500, 100, 100), ImageCrop.Clamp(800, 600, 700, 500, 400, 400));   // cut at the edges
        Assert.Equal(new ImageCrop.Rect(0, 0, 90, 80), ImageCrop.Clamp(800, 600, -10, -20, 100, 100));          // a negative start is pulled in
    }

    [Theory]
    [InlineData(800, 600, 0, 0, 800, 600)]      // the whole picture is a copy, not a crop
    [InlineData(800, 600, 800, 0, 100, 100)]    // starts outside
    [InlineData(800, 600, 0, 0, 10, 300)]       // a sliver
    [InlineData(800, 600, 795, 0, 100, 100)]    // a sliver once cut at the edge
    [InlineData(0, 0, 0, 0, 100, 100)]          // size unknown
    public void A_crop_that_makes_no_sense_is_refused(int iw, int ih, int x, int y, int w, int h) =>
        Assert.Null(ImageCrop.Clamp(iw, ih, x, y, w, h));

    [Theory]
    [InlineData(1600, 900, 1, 1, 350, 0, 900, 900)]
    [InlineData(900, 1600, 1, 1, 0, 350, 900, 900)]
    [InlineData(1600, 900, 16, 9, 0, 0, 1600, 900)]
    [InlineData(1000, 1000, 1200, 630, 0, 237, 1000, 525)]
    public void A_shape_is_the_largest_that_fits_centred(int iw, int ih, int rw, int rh, int x, int y, int w, int h) =>
        Assert.Equal(new ImageCrop.Rect(x, y, w, h), ImageCrop.Centered(iw, ih, rw, rh));

    [Fact]
    public void Both_actions_are_protected_audited_and_the_replace_is_confirmed_with_its_consequence()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var controller = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "MediaController.cs"));
        foreach (var route in new[] { "[HttpPost(\"details/{id:guid}/crop\")]", "[HttpPost(\"details/{id:guid}/replace\")]" })
        {
            var action = controller[controller.IndexOf(route, StringComparison.Ordinal)..];
            action = action[..action.IndexOf("public async Task<IActionResult>", StringComparison.Ordinal)];
            Assert.Contains("[ValidateAntiForgeryToken]", action);
        }
        Assert.Contains("_media.GetByIdAsync(id, userId)", controller);                 // only the uploader's own files
        Assert.Contains("_responsive.FileOf(item.Url)", controller);                    // never a path outside uploads
        Assert.Contains("Cropped from", controller);
        Assert.Contains("ReplacedWith", controller);
        Assert.Contains("SvgSanitizer.Sanitize", controller[controller.IndexOf("/replace", StringComparison.Ordinal)..]);

        var view = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Views", "Media", "Details.cshtml"));
        Assert.Contains("data-confirm-title=\"Replace this file?\"", view);
        Assert.Contains("The current file is not kept and cannot be brought back.", view);
        Assert.Contains("data-confirm-action=\"Replace file\"", view);
        // The crop and replace forms sit outside the tracked details form.
        Assert.True(view.IndexOf("id=\"cropForm\"", StringComparison.Ordinal) > view.IndexOf("</form>", StringComparison.Ordinal));
    }

    [Fact]
    public void The_crop_area_can_be_dragged_and_the_numbers_stay_the_source_of_truth()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var view = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Views", "Media", "Details.cshtml"));
        Assert.Contains("stage.addEventListener('pointerdown'", view);          // mouse, touch and pen alike
        Assert.Contains("f.w.value = w; f.h.value = h;", view);                 // a drag only fills the four fields that are posted
        Assert.Contains("if (w < minSide || h < minSide) return;", view);       // a click or a tap is not a crop: the numbers are left alone
        Assert.Contains("data-min-side=\"@Blog.Core.Services.ImageCrop.MinSide\"", view);
        Assert.Contains("id=\"cropX\" name=\"x\"", view);                       // which stay, labelled, for the keyboard
        var css = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "wwwroot", "css", "admin.css"));
        Assert.Contains(".desk-crop-stage { touch-action: pan-y;", css);        // a finger can still scroll the page past the picture
    }

    [Fact]
    public async Task A_document_is_replaced_only_by_the_same_kind_of_file()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddAuthentication(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName)
             .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminPagesSmokeTests.TestAdminAuthHandler>(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName, _ => { });
            s.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
                o.DefaultChallengeScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
            });
        }));
        using var scope = factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        if (AdminPagesSmokeTests.TestAdminAuthHandler.UserId == Guid.Empty) AdminPagesSmokeTests.TestAdminAuthHandler.UserId = admin.Id;
        var me = AdminPagesSmokeTests.TestAdminAuthHandler.UserId;
        var media = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);

        var tag = Guid.NewGuid().ToString("N")[..10];
        var subdir = "images/zz-doc-" + tag;
        var dirOnDisk = Path.Combine(env.WebRootPath, "uploads", "images", "zz-doc-" + tag);
        Directory.CreateDirectory(dirOnDisk);
        var url = $"/uploads/{subdir}/price-list.pdf";
        var onDisk = Path.Combine(dirOnDisk, "price-list.pdf");
        try
        {
            await File.WriteAllBytesAsync(onDisk, "%PDF-1.4 old"u8.ToArray());
            var id = await media.CreateAsync(new Blog.Core.Domain.Media
            {
                FileName = "price-list.pdf", OriginalFileName = "zz price list " + tag + ".pdf", FilePath = url, Url = url,
                ContentType = "application/pdf", FileSize = 12, UploadedBy = me,
            });

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var page = await client.GetStringAsync("/admin/media/details/" + id);
            Assert.Contains("File Details", page);
            Assert.Contains("Replace the file", page);
            Assert.DoesNotContain("Crop to a copy", page);                       // a document has nothing to crop
            Assert.Contains("accept=\".pdf\"", page);
            var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

            MultipartFormDataContent Form(byte[] bytes, string type, string name)
            {
                var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
                var part = new ByteArrayContent(bytes);
                part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
                form.Add(part, "file", name);
                return form;
            }

            // A picture, or a page dressed up with a .pdf name, does not replace a PDF.
            using var png = new MemoryStream();
            using (var picture = new Image<Rgba32>(20, 20)) await picture.SaveAsPngAsync(png);
            await client.PostAsync($"/admin/media/details/{id}/replace", Form(png.ToArray(), "image/png", "sneaky.pdf"));
            await client.PostAsync($"/admin/media/details/{id}/replace", Form("<html>"u8.ToArray(), "text/html", "page.pdf"));
            Assert.Equal("%PDF-1.4 old", await File.ReadAllTextAsync(onDisk));

            // Another PDF does: same address, new bytes, new size on the row.
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync($"/admin/media/details/{id}/replace", Form("%PDF-1.4 new and longer"u8.ToArray(), "application/pdf", "2027 prices.pdf"))).StatusCode);
            Assert.Equal("%PDF-1.4 new and longer", await File.ReadAllTextAsync(onDisk));
            var after = (await media.GetByIdAsync(id, me))!;
            Assert.Equal(url, after.Url);
            Assert.Equal(23, after.FileSize);
            Assert.False(File.Exists(onDisk + ".replacing"));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Media WHERE Url LIKE @p", new { p = $"/uploads/{subdir}/%" });
            try { Directory.Delete(dirOnDisk, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Cropping_saves_a_copy_and_replacing_keeps_the_address()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddAuthentication(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName)
             .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminPagesSmokeTests.TestAdminAuthHandler>(AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName, _ => { });
            s.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
                o.DefaultChallengeScheme = AdminPagesSmokeTests.TestAdminAuthHandler.SchemeName;
            });
        }));
        using var scope = factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        if (AdminPagesSmokeTests.TestAdminAuthHandler.UserId == Guid.Empty) AdminPagesSmokeTests.TestAdminAuthHandler.UserId = admin.Id;
        var me = AdminPagesSmokeTests.TestAdminAuthHandler.UserId;      // the files must belong to whoever the test signs in as
        var media = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);

        var tag = Guid.NewGuid().ToString("N")[..10];
        var subdir = "images/zz-test-" + tag;
        var dirOnDisk = Path.Combine(env.WebRootPath, "uploads", "images", "zz-test-" + tag);
        Directory.CreateDirectory(dirOnDisk);
        var url = $"/uploads/{subdir}/original.webp";
        try
        {
            using (var picture = new Image<Rgba32>(400, 300, new Rgba32(200, 30, 30)))
                await picture.SaveAsWebpAsync(Path.Combine(dirOnDisk, "original.webp"));
            var id = await media.CreateAsync(new Blog.Core.Domain.Media
            {
                FileName = "original.webp", OriginalFileName = "zz " + tag + ".webp", FilePath = url, Url = url,
                ContentType = "image/webp", FileSize = new FileInfo(Path.Combine(dirOnDisk, "original.webp")).Length, Width = 400, Height = 300, UploadedBy = me,
            });
            await media.UpdateDetailsAsync(id, me, "A red rectangle", null);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var page = await client.GetStringAsync("/admin/media/details/" + id);
            Assert.Contains("Crop to a copy", page);
            Assert.Contains("Replace the file", page);
            var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEqual("", token);

            // Crop: a new row, the original untouched.
            var originalBytes = await File.ReadAllBytesAsync(Path.Combine(dirOnDisk, "original.webp"));
            var crop = await client.PostAsync($"/admin/media/details/{id}/crop", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["x"] = "50", ["y"] = "50", ["width"] = "200", ["height"] = "100",
            }));
            Assert.Equal(HttpStatusCode.Redirect, crop.StatusCode);
            var copyId = Guid.Parse(Regex.Match(crop.Headers.Location!.ToString(), "details/([0-9a-f-]{36})").Groups[1].Value);
            Assert.NotEqual(id, copyId);
            var copy = (await media.GetByIdAsync(copyId, me))!;
            Assert.Equal((200, 100), (copy.Width, copy.Height));
            Assert.Equal("A red rectangle", copy.AltText);
            Assert.StartsWith($"/uploads/{subdir}/", copy.Url);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Path.Combine(dirOnDisk, "original.webp")));

            // A crop of the whole picture is refused and creates nothing.
            var countBefore = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Media WHERE Url LIKE @p", new { p = $"/uploads/{subdir}/%" });
            await client.PostAsync($"/admin/media/details/{id}/crop", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["x"] = "0", ["y"] = "0", ["width"] = "400", ["height"] = "300",
            }));
            Assert.Equal(countBefore, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Media WHERE Url LIKE @p", new { p = $"/uploads/{subdir}/%" }));

            // Replace: same address, new picture, new dimensions recorded.
            using var png = new MemoryStream();
            using (var replacement = new Image<Rgba32>(120, 80, new Rgba32(20, 20, 220))) await replacement.SaveAsPngAsync(png);
            using var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
            var filePart = new ByteArrayContent(png.ToArray());
            filePart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(filePart, "file", "new.png");
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync($"/admin/media/details/{id}/replace", form)).StatusCode);

            var after = (await media.GetByIdAsync(id, me))!;
            Assert.Equal(url, after.Url);
            Assert.Equal((120, 80), (after.Width, after.Height));
            using (var stored = await Image.LoadAsync(Path.Combine(dirOnDisk, "original.webp")))
                Assert.Equal((120, 80), (stored.Width, stored.Height));                 // still a WebP at the same address
            Assert.Equal("A red rectangle", after.AltText);

            // A file of another kind is refused and the picture stays.
            using var wrong = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
            var pdf = new ByteArrayContent("%PDF-1.4"u8.ToArray());
            pdf.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            wrong.Add(pdf, "file", "doc.pdf");
            await client.PostAsync($"/admin/media/details/{id}/replace", wrong);
            using (var stored = await Image.LoadAsync(Path.Combine(dirOnDisk, "original.webp")))
                Assert.Equal((120, 80), (stored.Width, stored.Height));
            Assert.False(File.Exists(Path.Combine(dirOnDisk, "original.webp.replacing")));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Media WHERE Url LIKE @p", new { p = $"/uploads/{subdir}/%" });
            try { Directory.Delete(dirOnDisk, recursive: true); } catch { /* best effort */ }
        }
    }
}
