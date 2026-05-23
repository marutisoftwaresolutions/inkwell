using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;
using IOFile  = System.IO.File;
using IODir   = System.IO.Directory;

// ── Output directory ───────────────────────────────────────────────────────────
var baseDir = IOPath.GetFullPath(
    IOPath.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                   "Blog.Web", "wwwroot", "uploads", "features"));
IODir.CreateDirectory(baseDir);
Console.WriteLine($"Output → {baseDir}");

// ── Font ───────────────────────────────────────────────────────────────────────
FontFamily font = default;
foreach (var name in new[] { "Segoe UI", "Arial", "Helvetica", "Liberation Sans", "DejaVu Sans" })
    if (SystemFonts.TryGet(name, out font)) break;
if (font.Name is null) font = SystemFonts.Families.First();

// ── Post definitions ───────────────────────────────────────────────────────────
var posts = new[]
{
    new PostSpec(
        "best-optometry-ehr-software-2026",
        "Best Optometry EHR Software in 2026: 7 Systems Compared",
        "ULTIMATE GUIDE", "EHR & Electronic Records",
        "0f172a", "1e3a5f", "f59e0b", ShapeStyle.Grid),

    new PostSpec(
        "eyefinity-ehr-review",
        "Eyefinity EHR Review 2026: Features, Pricing & Verdict",
        "PRODUCT REVIEW", "EHR & Electronic Records",
        "1e3b8a", "1e40af", "60a5fa", ShapeStyle.Circles),

    new PostSpec(
        "revolution-ehr-review",
        "RevolutionEHR Review 2026: Is It Right for Your Practice?",
        "PRODUCT REVIEW", "EHR & Electronic Records",
        "3b0764", "5b21b6", "c084fc", ShapeStyle.Circles),

    new PostSpec(
        "compulink-optometry-review",
        "Compulink Optometry Review 2026: Enterprise EHR Strengths & Weaknesses",
        "PRODUCT REVIEW", "EHR & Electronic Records",
        "1e1b4b", "312e81", "818cf8", ShapeStyle.Dots),

    new PostSpec(
        "crystal-pm-vs-officemate",
        "Crystal PM vs OfficeMate: Which Practice Management System Should You Choose?",
        "HEAD-TO-HEAD", "Practice Management Software",
        "4a044e", "86198f", "e879f9", ShapeStyle.Versus),

    new PostSpec(
        "maximeyes-ehr-review",
        "MaximEyes EHR Review 2026: Features for Multi-Location Practices",
        "PRODUCT REVIEW", "EHR & Electronic Records",
        "064e3b", "0f766e", "34d399", ShapeStyle.Circles),

    new PostSpec(
        "imedicware-optometry-review",
        "iMedicWare Optometry EHR Review 2026: Telehealth-Forward Platform",
        "PRODUCT REVIEW", "Teleoptometry & Remote Care",
        "0c4a6e", "0369a1", "38bdf8", ShapeStyle.Dots),

    new PostSpec(
        "cloud-vs-server-optometry-ehr",
        "Cloud-Based vs Server-Based Optometry EHR: Which Wins in 2026?",
        "COMPARISON", "EHR & Electronic Records",
        "1c1917", "292524", "fb923c", ShapeStyle.Versus),

    new PostSpec(
        "how-to-switch-optometry-ehr",
        "How to Switch Optometry EHR Software Without Losing Data",
        "HOW-TO GUIDE", "EHR & Electronic Records",
        "14532d", "166534", "4ade80", ShapeStyle.Steps),

    new PostSpec(
        "optometry-ehr-implementation-checklist",
        "Optometry EHR Implementation Checklist: Your 30-Day Go-Live Plan",
        "CHECKLIST", "Practice Management Software",
        "134e4a", "115e59", "2dd4bf", ShapeStyle.Steps),

    new PostSpec(
        "hipaa-compliant-ehr-optometrists-2026",
        "HIPAA-Compliant EHR for Optometrists: 2026 Compliance Checklist",
        "COMPLIANCE", "EHR & Electronic Records",
        "0f172a", "1e3a5f", "fbbf24", ShapeStyle.Grid),

    new PostSpec(
        "best-optometry-practice-management-software-2026",
        "Best Optometry Practice Management Software in 2026: Top 8 Compared",
        "ULTIMATE GUIDE", "Practice Management Software",
        "312e81", "4338ca", "a78bfa", ShapeStyle.Steps),

    new PostSpec(
        "optometry-billing-software-reduce-claim-denials-2026",
        "Optometry Billing Software: How to Reduce Claim Denials in 2026",
        "BEST PRACTICE", "Billing & Revenue Cycle",
        "064e3b", "0f766e", "34d399", ShapeStyle.Steps),

    new PostSpec(
        "best-contact-lens-fitting-software-optometrists-2026",
        "Best Contact Lens Fitting Software for Optometrists in 2026",
        "BUYER'S GUIDE", "Contact Lens Fitting Technology",
        "0c4a6e", "0284c7", "38bdf8", ShapeStyle.Circles),

    new PostSpec(
        "optical-retail-software-increase-frame-sales",
        "Optical Retail Software: Increase Frame Sales and Inventory Accuracy",
        "RETAIL GUIDE", "Optical Retail Technology",
        "450a0a", "7f1d1d", "f43f5e", ShapeStyle.Grid),

    new PostSpec(
        "teleoptometry-platforms-compared-2026",
        "Teleoptometry Platforms Compared: Best Remote Eye Care Tools in 2026",
        "COMPARISON", "Teleoptometry & Remote Care",
        "1e1b4b", "312e81", "818cf8", ShapeStyle.Versus),

    new PostSpec(
        "frame-lens-inventory-management-software-optical-dispensaries",
        "Frame and Lens Inventory Management Software for Optical Dispensaries",
        "INVENTORY GUIDE", "Frame & Lens Design Software",
        "1c1917", "44403c", "fb923c", ShapeStyle.Steps),

    new PostSpec(
        "ai-powered-optometry-machine-learning-eye-care-2026",
        "AI-Powered Optometry: How Machine Learning is Changing Eye Care in 2026",
        "TECH TRENDS", "AI & Machine Learning in Optometry",
        "172554", "1e3a8a", "60a5fa", ShapeStyle.Dots),

    new PostSpec(
        "best-digital-retinal-cameras-optometry-2026",
        "Best Digital Retinal Cameras for Optometry Practices in 2026",
        "HARDWARE REVIEW", "Digital Imaging & Diagnostics",
        "0f172a", "334155", "10b981", ShapeStyle.Circles),

    new PostSpec(
        "vision-therapy-software-management-tools-ods-2026",
        "Vision Therapy Software: Top Management Tools for ODs in 2026",
        "CLINICAL TOOLS", "Vision Therapy Technology",
        "064e3b", "0f766e", "a3e635", ShapeStyle.Grid),

    new PostSpec(
        "officemate-ehr-review-2026",
        "OfficeMate EHR Review 2026: Complete Analysis for Optometrists",
        "PRODUCT REVIEW", "EHR & Electronic Records",
        "1e3b8a", "1e40af", "60a5fa", ShapeStyle.Circles),

    new PostSpec(
        "patient-communication-tools-optometry-recalls-reminders",
        "Patient Communication Tools for Optometry: Recalls, Reminders, and Reviews",
        "PRACTICE GROWTH", "Practice Management Software",
        "4a044e", "86198f", "e879f9", ShapeStyle.Dots),

    new PostSpec(
        "icd-10-cpt-codes-optometry-2026-reference-guide",
        "ICD-10 and CPT Codes for Optometry: 2026 Complete Reference Guide",
        "REFERENCE GUIDE", "Billing & Revenue Cycle",
        "064e3b", "115e59", "2dd4bf", ShapeStyle.Grid),

    new PostSpec(
        "contact-lens-practice-builder-software-grow-revenue",
        "Contact Lens Practice Builder: Software Tools to Grow CL Revenue",
        "REVENUE STRATEGY", "Contact Lens Fitting Technology",
        "0c4a6e", "0369a1", "38bdf8", ShapeStyle.Steps),

    new PostSpec(
        "optical-pos-systems-eye-care-stores-2026",
        "Optical POS Systems: Best Point of Sale Software for Eye Care Stores in 2026",
        "BUYER'S GUIDE", "Optical Retail Technology",
        "450a0a", "7f1d1d", "fb923c", ShapeStyle.Versus),

    new PostSpec(
        "teleoptometry-reimbursement-guide-state-by-state",
        "How to Get Reimbursed for Teleoptometry Services: State-by-State Guide",
        "BILLING GUIDE", "Teleoptometry & Remote Care",
        "1e1b4b", "312e81", "a78bfa", ShapeStyle.Steps),

    new PostSpec(
        "anti-reflective-lens-coating-track-upsell-optometry-software",
        "Anti-Reflective Lens Coating: How to Track and Upsell with Optometry Software",
        "RETAIL GUIDE", "Frame & Lens Design Software",
        "1c1917", "44403c", "f59e0b", ShapeStyle.Versus),

    new PostSpec(
        "glaucoma-detection-software-ai-tools-optometrists-2026",
        "Glaucoma Detection Software: AI Tools Every Optometrist Should Know in 2026",
        "AI IN CLINIC", "AI & Machine Learning in Optometry",
        "172554", "1e3a8a", "f43f5e", ShapeStyle.Circles),

    new PostSpec(
        "oct-vs-fundus-photography-optometry",
        "OCT vs Fundus Photography in Optometry: Which Is Right for Your Practice?",
        "CLINICAL TECH", "Digital Imaging & Diagnostics",
        "0f172a", "334155", "fb923c", ShapeStyle.Versus),

    new PostSpec(
        "treating-amblyopia-children-software-track-progress-2026",
        "Treating Amblyopia in Children: Software Tools to Track Progress in 2026",
        "PEDIATRICS", "Vision Therapy Technology",
        "064e3b", "0f766e", "34d399", ShapeStyle.Circles),

    new PostSpec(
        "ehr-interoperability-optometrists-connecting-medical-systems",
        "EHR Interoperability for Optometrists: Connecting with Medical Systems",
        "INTEGRATIONS", "EHR & Electronic Records",
        "0f172a", "1e3a5f", "fbbf24", ShapeStyle.Dots),

    new PostSpec(
        "optometry-practice-analytics-grow-eye-care-business",
        "Optometry Practice Analytics: Using Data to Grow Your Eye Care Business",
        "ANALYTICS", "Practice Management Software",
        "312e81", "4338ca", "a78bfa", ShapeStyle.Grid),

    new PostSpec(
        "medical-vs-vision-insurance-billing-optometry-guide",
        "Medical vs Vision Insurance Billing in Optometry: A Complete Guide",
        "INSURANCE GUIDE", "Billing & Revenue Cycle",
        "064e3b", "0f766e", "fbbf24", ShapeStyle.Versus),

    new PostSpec(
        "contact-lens-inventory-management-software-optometry",
        "Contact Lens Inventory Management: Best Software for Optometry Practices",
        "CLINIC OPERATION", "Contact Lens Fitting Technology",
        "0c4a6e", "0284c7", "2dd4bf", ShapeStyle.Grid),

    new PostSpec(
        "diabetic-eye-exam-ai-screening-tools-optometrists-2026",
        "Diabetic Eye Exam AI Screening Tools for Optometrists in 2026",
        "AI IN CLINIC", "AI & Machine Learning in Optometry",
        "172554", "1e3a8a", "34d399", ShapeStyle.Steps),
};

// ── Generate ───────────────────────────────────────────────────────────────────
var pngMappings = new System.Collections.Generic.Dictionary<string, string>
{
    { "hipaa-compliant-ehr-optometrists-2026", "hipaa_ehr_compliance_1779510021438.png" },
    { "best-optometry-practice-management-software-2026", "optometry_practice_software_1779510053274.png" },
    { "optometry-billing-software-reduce-claim-denials-2026", "optometry_billing_software_1779510068693.png" },
    { "best-contact-lens-fitting-software-optometrists-2026", "contact_lens_fitting_1779510083450.png" },
    { "optical-retail-software-increase-frame-sales", "optical_retail_software_1779510097572.png" },
    { "teleoptometry-platforms-compared-2026", "teleoptometry_platforms_1779510112313.png" },
    { "frame-lens-inventory-management-software-optical-dispensaries", "inventory_management_software_1779510133471.png" },
    { "ai-powered-optometry-machine-learning-eye-care-2026", "ai_powered_optometry_1779510153689.png" },
    { "best-digital-retinal-cameras-optometry-2026", "digital_retinal_camera_1779510168359.png" },
    { "vision-therapy-software-management-tools-ods-2026", "vision_therapy_software_1779510183872.png" },
    { "officemate-ehr-review-2026", "officemate_ehr_review_1779510199162.png" },
    { "patient-communication-tools-optometry-recalls-reminders", "patient_communication_tools_1779510220770.png" },
    { "icd-10-cpt-codes-optometry-2026-reference-guide", "icd10_cpt_codes_1779510235532.png" },
    { "contact-lens-practice-builder-software-grow-revenue", "contact_lens_builder_1779510251085.png" },
    { "optical-pos-systems-eye-care-stores-2026", "optical_pos_system_1779510266345.png" },
    { "teleoptometry-reimbursement-guide-state-by-state", "teleoptometry_reimbursement_1779510279336.png" },
    { "anti-reflective-lens-coating-track-upsell-optometry-software", "anti_reflective_coating_1779510299776.png" }
};

var unsplashUrls = new System.Collections.Generic.Dictionary<string, string>
{
    { "glaucoma-detection-software-ai-tools-optometrists-2026", "https://images.unsplash.com/photo-1576091160550-2173dba999ef?auto=format&fit=crop&w=1200&h=630&q=80" },
    { "oct-vs-fundus-photography-optometry", "https://images.unsplash.com/photo-1507679799987-c73779587ccf?auto=format&fit=crop&w=1200&h=630&q=80" },
    { "treating-amblyopia-children-software-track-progress-2026", "https://images.unsplash.com/photo-1516627145497-ae6968895b74?auto=format&fit=crop&w=1200&h=630&q=80" },
    { "ehr-interoperability-optometrists-connecting-medical-systems", "https://images.unsplash.com/photo-1558494949-ef010cbdcc31?auto=format&fit=crop&w=1200&h=630&q=80" },
    { "optometry-practice-analytics-grow-eye-care-business", "https://images.unsplash.com/photo-1551288049-bebda4e38f71?auto=format&fit=crop&w=1200&h=630&q=80" },
    { "medical-vs-vision-insurance-billing-optometry-guide", "https://images.unsplash.com/photo-1454165804606-c3d57bc86b40?auto=format&fit=crop&w=1200&h=630&q=80" },
    { "contact-lens-inventory-management-software-optometry", "https://images.unsplash.com/photo-1574269909862-7e1d70bb8078?auto=format&fit=crop&w=1200&h=630&q=80" },
    { "diabetic-eye-exam-ai-screening-tools-optometrists-2026", "https://images.unsplash.com/photo-1581091226825-a6a2a5aee158?auto=format&fit=crop&w=1200&h=630&q=80" }
};

using var httpClient = new System.Net.Http.HttpClient();
httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

for (int i = 0; i < posts.Length; i++)
{
    var p = posts[i];
    var outPath = IOPath.Combine(baseDir, $"{p.Slug}.jpg");

    if (i < 10)
    {
        GenerateImage(p, font, outPath);
        Console.WriteLine($"  [+] {p.Slug}.jpg (Abstract Card)");
    }
    else
    {
        if (pngMappings.TryGetValue(p.Slug, out var localPngName))
        {
            var sourcePath = IOPath.Combine(@"C:\Users\Admin\.gemini\antigravity\brain\7478d376-9dcb-49a1-baa8-8e44dcde0cad", localPngName);
            if (IOFile.Exists(sourcePath))
            {
                using var image = Image.Load(sourcePath);
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(1200, 630),
                    Mode = ResizeMode.Crop
                }));
                image.Save(outPath, new JpegEncoder { Quality = 90 });
                Console.WriteLine($"  [+] {p.Slug}.jpg (From custom AI illustration)");
            }
            else
            {
                Console.WriteLine($"  [!] Source PNG not found: {sourcePath}. Generating fallback card...");
                GenerateImage(p, font, outPath);
            }
        }
        else if (unsplashUrls.TryGetValue(p.Slug, out var url))
        {
            try
            {
                var imageBytes = httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();
                using var ms = new System.IO.MemoryStream(imageBytes);
                using var image = Image.Load(ms);
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(1200, 630),
                    Mode = ResizeMode.Crop
                }));
                image.Save(outPath, new JpegEncoder { Quality = 90 });
                Console.WriteLine($"  [+] {p.Slug}.jpg (From relevant stock photo)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [!] Failed to download {url}: {ex.Message}. Generating fallback card...");
                GenerateImage(p, font, outPath);
            }
        }
        else
        {
            GenerateImage(p, font, outPath);
            Console.WriteLine($"  [+] {p.Slug}.jpg (Abstract Card Fallback)");
        }
    }
}

Console.WriteLine($"\nDone — {posts.Length} images generated/processed.");

// ── Write companion SQL script ─────────────────────────────────────────────────
var sqlPath = IOPath.GetFullPath(
    IOPath.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                   "feature-images-update.sql"));

var sb = new System.Text.StringBuilder();
sb.AppendLine("-- =================================================================");
sb.AppendLine("-- feature-images-update.sql");
sb.AppendLine("-- Updates FeatureImage for all 10 Week-1 EHR posts.");
sb.AppendLine("-- Run AFTER FeatureImageGenerator has run and populated");
sb.AppendLine("-- Blog.Web/wwwroot/uploads/features/");
sb.AppendLine("-- =================================================================");
sb.AppendLine();
foreach (var p in posts)
    sb.AppendLine($"UPDATE Posts SET FeatureImage = N'/uploads/features/{p.Slug}.jpg'  WHERE Slug = N'{p.Slug}';");

sb.AppendLine();
sb.AppendLine("-- Verify");
sb.AppendLine("SELECT Slug, FeatureImage FROM Posts WHERE FeatureImage LIKE '/uploads/features/%' ORDER BY Slug;");

IOFile.WriteAllText(sqlPath, sb.ToString());
Console.WriteLine($"SQL  → {sqlPath}");

// ── Logo + Favicon ─────────────────────────────────────────────────────────────
var wwwroot    = IOPath.GetFullPath(IOPath.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Blog.Web", "wwwroot"));
var logoPath   = IOPath.Combine(wwwroot, "logo.png");
var faviconPath= IOPath.Combine(wwwroot, "favicon.ico");

GenerateLogo(font, logoPath);
Console.WriteLine($"  [+] logo.png");

GenerateFavicon(faviconPath);
Console.WriteLine($"  [+] favicon.ico");

var imagesDir  = IOPath.Combine(wwwroot, "uploads", "images");
IODir.CreateDirectory(imagesDir);
var coverPath  = IOPath.Combine(imagesDir, "site-cover.jpg");
GenerateCover(font, coverPath);
Console.WriteLine($"  [+] uploads/images/site-cover.jpg");

// Settings SQL
var settingsSqlPath = IOPath.GetFullPath(IOPath.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "branding-settings-update.sql"));
var ssql = new System.Text.StringBuilder();
ssql.AppendLine("-- =================================================================");
ssql.AppendLine("-- branding-settings-update.sql");
ssql.AppendLine("-- Updates site name, tagline, logo, and favicon in Settings JSON.");
ssql.AppendLine("-- Targets the single Settings row for the self-hosted owner.");
ssql.AppendLine("-- =================================================================");
ssql.AppendLine();
ssql.AppendLine("UPDATE Settings");
ssql.AppendLine("SET JsonPayload = JSON_MODIFY(");
ssql.AppendLine("                  JSON_MODIFY(");
ssql.AppendLine("                   JSON_MODIFY(");
ssql.AppendLine("                    JSON_MODIFY(");
ssql.AppendLine("                     JSON_MODIFY(JsonPayload,");
ssql.AppendLine("                       '$.SiteName',        N'Optical Software'),");
ssql.AppendLine("                       '$.SiteDescription', N'Independent Reviews & Guides for Optometry Software'),");
ssql.AppendLine("                       '$.SiteLogoUrl',     N'/logo.png'),");
ssql.AppendLine("                       '$.SiteFaviconUrl',  N'/favicon.ico'),");
ssql.AppendLine("                       '$.SiteCoverUrl',    N'/uploads/images/site-cover.jpg'),");
ssql.AppendLine("    UpdatedAt = GETUTCDATE()");
ssql.AppendLine("WHERE UserId IS NOT NULL;  -- applies to whichever row holds site-wide settings");
ssql.AppendLine();
ssql.AppendLine("-- Verify");
ssql.AppendLine("SELECT JSON_VALUE(JsonPayload,'$.SiteName')        AS SiteName,");
ssql.AppendLine("       JSON_VALUE(JsonPayload,'$.SiteDescription') AS Tagline,");
ssql.AppendLine("       JSON_VALUE(JsonPayload,'$.SiteLogoUrl')     AS Logo,");
ssql.AppendLine("       JSON_VALUE(JsonPayload,'$.SiteFaviconUrl')  AS Favicon,");
ssql.AppendLine("       JSON_VALUE(JsonPayload,'$.SiteCoverUrl')    AS Cover");
ssql.AppendLine("FROM   Settings;");
IOFile.WriteAllText(settingsSqlPath, ssql.ToString());
Console.WriteLine($"SQL  → {settingsSqlPath}");

// ── Image generation ───────────────────────────────────────────────────────────
static void GenerateImage(PostSpec p, FontFamily fontFamily, string outPath)
{
    const int W = 1200, H = 630;

    var bgTop   = Color.ParseHex(p.BgTop);
    var bgBot   = Color.ParseHex(p.BgBottom);
    var accent  = Color.ParseHex(p.Accent);
    var white   = Color.White;
    var whiteMid = Color.FromRgba(255, 255, 255, 200);
    var whiteDim = Color.FromRgba(255, 255, 255, 110);

    var badgeFont    = fontFamily.CreateFont(21, FontStyle.Bold);
    var categoryFont = fontFamily.CreateFont(23, FontStyle.Regular);
    var titleFont    = fontFamily.CreateFont(60, FontStyle.Bold);
    var siteFont     = fontFamily.CreateFont(25, FontStyle.Bold);

    using var img = new Image<Rgba32>(W, H);
    img.Mutate(ctx =>
    {
        // Background
        ctx.Fill(new LinearGradientBrush(
            new PointF(0, 0), new PointF(W, H),
            GradientRepetitionMode.None,
            new ColorStop(0f, bgTop),
            new ColorStop(1f, bgBot)));

        // Decorative shapes
        DrawShapes(ctx, p.Shape, accent, W, H);

        // Left accent bar
        ctx.Fill(accent, new RectangleF(0, 0, 10, H));

        // Badge pill
        var badgeText = p.Badge;
        var bMeasure  = TextMeasurer.MeasureSize(badgeText, new TextOptions(badgeFont));
        float bx = 52, by = 44, bw = bMeasure.Width + 30, bh = 36;
        ctx.Fill(accent, new RectangleF(bx, by, bw, bh));
        ctx.DrawText(new RichTextOptions(badgeFont) { Origin = new PointF(bx + 15, by + 7) }, badgeText, Color.Black);

        // Category
        ctx.DrawText(new RichTextOptions(categoryFont) { Origin = new PointF(52, by + bh + 16) },
            p.Category.ToUpperInvariant(), whiteDim);

        // Title
        var titleY = by + bh + 58f;
        var title  = p.Title.Length > 100 ? p.Title[..97] + "…" : p.Title;
        ctx.DrawText(new RichTextOptions(titleFont)
        {
            Origin = new PointF(52, titleY),
            WrappingLength = W - 110,
            LineSpacing = 1.15f,
        }, title, white);

        // Bottom strip
        ctx.Fill(Color.FromRgba(0, 0, 0, 90), new RectangleF(0, H - 68, W, 68));
        ctx.Fill(accent, new RectangleF(10, H - 68, W - 10, 2));

        // Site domain
        ctx.DrawText(new RichTextOptions(siteFont) { Origin = new PointF(52, H - 48) },
            "opticalsoftware.org", whiteMid);

        // Small accent dots — bottom right
        for (int i = 0; i < 3; i++)
            ctx.Fill(Color.FromRgba(255, 255, 255, 35),
                new EllipsePolygon(W - 100 + i * 28f, H - 34, 9 - i * 2, 9 - i * 2));
    });

    img.Save(outPath, new JpegEncoder { Quality = 90 });
}

static void DrawShapes(IImageProcessingContext ctx, ShapeStyle style, Color accent, int W, int H)
{
    var ap = accent.ToPixel<Rgba32>();
    var faint  = Color.FromRgba(ap.R, ap.G, ap.B, 22);
    var medium = Color.FromRgba(ap.R, ap.G, ap.B, 55);

    switch (style)
    {
        case ShapeStyle.Circles:
            ctx.Fill(faint,  new EllipsePolygon(W - 100f, 60f, 280f, 280f));
            ctx.Fill(faint,  new EllipsePolygon(W + 40f, H - 40f, 240f, 240f));
            ctx.Fill(medium, new EllipsePolygon(W - 180f, 240f, 140f, 140f));
            break;

        case ShapeStyle.Dots:
            var rng = new Random(42);
            for (int i = 0; i < 38; i++)
            {
                float dx = rng.Next(W / 2, W - 20);
                float dy = rng.Next(20, H - 20);
                float r  = rng.Next(3, 9);
                ctx.Fill(faint, new EllipsePolygon(dx, dy, r, r));
            }
            ctx.Fill(medium, new EllipsePolygon(W - 150f, H / 2f, 75f, 75f));
            break;

        case ShapeStyle.Grid:
            for (int gx = W / 2; gx < W; gx += 60)
                ctx.Fill(faint, new RectangleF(gx, 0, 1, H));
            for (int gy = 0; gy < H; gy += 60)
                ctx.Fill(faint, new RectangleF(W / 2, gy, W / 2, 1));
            ctx.Fill(medium, new RectangleF(W - 200, 40, 160, 4));
            ctx.Fill(medium, new RectangleF(W - 200, 40, 4, 100));
            break;

        case ShapeStyle.Versus:
            // Diagonal slash bands
            IPath slash1 = new Polygon(new LinearLineSegment(
                new PointF(W - 310, 0), new PointF(W - 190, 0),
                new PointF(W + 60, H), new PointF(W - 60, H)));
            IPath slash2 = new Polygon(new LinearLineSegment(
                new PointF(W - 245, 0), new PointF(W - 215, 0),
                new PointF(W - 5, H),  new PointF(W - 35, H)));
            ctx.Fill(faint,  slash1);
            ctx.Fill(medium, slash2);
            break;

        case ShapeStyle.Steps:
            for (int s = 0; s < 5; s++)
            {
                float sx = W - 310 + s * 60f;
                float sy = H - 80 - s * 50f;
                ctx.Fill(s % 2 == 0 ? faint : medium, new RectangleF(sx, sy, 54, H - sy));
            }
            break;
    }
}

// ── Cover image generator (1500 × 500 JPEG) ───────────────────────────────────
static void GenerateCover(FontFamily fontFamily, string outPath)
{
    const int W = 1500, H = 500;

    var bgLeft   = Color.ParseHex("0f172a");   // near-black navy
    var bgRight  = Color.ParseHex("1e1b4b");   // deep indigo
    var indigo   = Color.ParseHex("4f46e5");
    var indigoLt = Color.ParseHex("818cf8");
    var white    = Color.White;
    var whiteMid = Color.FromRgba(255, 255, 255, 200);
    var whiteDim = Color.FromRgba(255, 255, 255, 100);

    var tagFont  = fontFamily.CreateFont(24, FontStyle.Regular);
    var h1Font   = fontFamily.CreateFont(72, FontStyle.Bold);
    var subFont  = fontFamily.CreateFont(28, FontStyle.Regular);
    var badgeFont= fontFamily.CreateFont(20, FontStyle.Bold);

    using var img = new Image<Rgba32>(W, H);
    img.Mutate(ctx =>
    {
        // ── Gradient background (left navy → right deep indigo) ──────────────
        ctx.Fill(new LinearGradientBrush(
            new PointF(0, 0), new PointF(W, H),
            GradientRepetitionMode.None,
            new ColorStop(0f, bgLeft),
            new ColorStop(1f, bgRight)));

        // ── Large decorative circles (right side, layered) ───────────────────
        var ap = indigo.ToPixel<Rgba32>();
        ctx.Fill(Color.FromRgba(ap.R, ap.G, ap.B, 18), new EllipsePolygon(W - 200f, H / 2f, 420f, 420f));
        ctx.Fill(Color.FromRgba(ap.R, ap.G, ap.B, 28), new EllipsePolygon(W - 100f, H / 2f, 280f, 280f));
        ctx.Fill(Color.FromRgba(ap.R, ap.G, ap.B, 40), new EllipsePolygon(W + 20f,  H / 2f, 200f, 200f));

        // ── Large eye symbol centered on the right ring cluster ──────────────
        float ex = W - 180f, ey = H / 2f;
        ctx.Fill(Color.FromRgba(ap.R, ap.G, ap.B, 60), new EllipsePolygon(ex, ey, 120f, 120f));
        ctx.Fill(Color.FromRgba(255, 255, 255, 50),     new EllipsePolygon(ex, ey, 80f,  80f));
        ctx.Fill(indigo,                                 new EllipsePolygon(ex, ey, 46f,  46f));
        ctx.Fill(Color.White,                            new EllipsePolygon(ex, ey, 18f,  18f));
        ctx.Fill(Color.FromRgba(79, 70, 229, 200),       new EllipsePolygon(ex, ey, 7f,   7f));
        // glint
        ctx.Fill(Color.White, new EllipsePolygon(ex + 18f, ey - 18f, 8f, 8f));

        // ── Floating small dots (decorative) ─────────────────────────────────
        var rng = new Random(77);
        for (int i = 0; i < 30; i++)
        {
            float dx = rng.Next(W / 2, W - 60);
            float dy = rng.Next(20, H - 20);
            float r  = rng.Next(2, 6);
            ctx.Fill(Color.FromRgba(255, 255, 255, (byte)rng.Next(15, 45)),
                new EllipsePolygon(dx, dy, r, r));
        }

        // ── Left accent bar ──────────────────────────────────────────────────
        ctx.Fill(indigo, new RectangleF(0, 0, 8, H));

        // ── "INDEPENDENT REVIEWS" badge ──────────────────────────────────────
        var badgeText = "INDEPENDENT REVIEWS & GUIDES";
        var bm = TextMeasurer.MeasureSize(badgeText, new TextOptions(badgeFont));
        float bx = 72, by = 80, bw = bm.Width + 28, bh = 34;
        ctx.Fill(indigo, new RectangleF(bx, by, bw, bh));
        ctx.DrawText(new RichTextOptions(badgeFont) { Origin = new PointF(bx + 14, by + 7) },
            badgeText, white);

        // ── Main headline ────────────────────────────────────────────────────
        ctx.DrawText(new RichTextOptions(h1Font)
        {
            Origin = new PointF(72, 142),
            WrappingLength = W * 0.58f,
            LineSpacing = 1.1f,
        }, "Optical\nSoftware", white);

        // ── Accent underline ─────────────────────────────────────────────────
        ctx.Fill(indigoLt, new RectangleF(72, 316, 200, 4));

        // ── Sub-tagline ──────────────────────────────────────────────────────
        ctx.DrawText(new RichTextOptions(subFont)
        {
            Origin = new PointF(72, 338),
            WrappingLength = W * 0.52f,
            LineSpacing = 1.3f,
        }, "Independent Reviews & Guides\nfor Optometry Software", whiteMid);

        // ── Domain watermark ─────────────────────────────────────────────────
        ctx.DrawText(new RichTextOptions(tagFont)
        {
            Origin = new PointF(72, H - 52),
        }, "opticalsoftware.org", whiteDim);

        // ── Bottom accent strip ──────────────────────────────────────────────
        ctx.Fill(indigo, new RectangleF(0, H - 4, W, 4));
    });

    img.Save(outPath, new JpegEncoder { Quality = 92 });
}

// ── Logo generator (600 × 150 transparent PNG) ────────────────────────────────
static void GenerateLogo(FontFamily fontFamily, string outPath)
{
    const int W = 600, H = 150;

    var indigo     = Color.ParseHex("4f46e5");
    var indigoLight= Color.ParseHex("818cf8");
    var darkText   = Color.ParseHex("0f172a");
    var subText    = Color.ParseHex("475569");

    var nameFont   = fontFamily.CreateFont(46, FontStyle.Bold);
    var domainFont = fontFamily.CreateFont(18, FontStyle.Regular);

    using var img = new Image<Rgba32>(W, H, Color.Transparent);
    img.Mutate(ctx =>
    {
        // ── Eye / lens icon (left, centered vertically) ──────────────────────
        // Outer circle — indigo filled
        ctx.Fill(indigo, new EllipsePolygon(72f, 75f, 58f, 58f));
        // Middle ring — white
        ctx.Fill(Color.White, new EllipsePolygon(72f, 75f, 40f, 40f));
        // Inner iris — indigo
        ctx.Fill(indigo, new EllipsePolygon(72f, 75f, 24f, 24f));
        // Pupil — near-white
        ctx.Fill(Color.FromRgba(255, 255, 255, 230), new EllipsePolygon(72f, 75f, 10f, 10f));
        // Highlight glint
        ctx.Fill(Color.White, new EllipsePolygon(82f, 65f, 5f, 5f));

        // ── "Optical Software" text ──────────────────────────────────────────
        ctx.DrawText(new RichTextOptions(nameFont)
        {
            Origin = new PointF(148, 26),
        }, "Optical Software", darkText);

        // Accent underline beneath the name
        ctx.Fill(indigoLight, new RectangleF(148, 86, 330, 3));

        // ── Domain sub-label ─────────────────────────────────────────────────
        ctx.DrawText(new RichTextOptions(domainFont)
        {
            Origin = new PointF(150, 98),
        }, "opticalsoftware.org  •  Independent Reviews & Guides", subText);
    });

    using var IOStream = new System.IO.MemoryStream();
    img.Save(IOStream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
    IOFile.WriteAllBytes(outPath, IOStream.ToArray());
}

// ── Favicon generator (32×32 embedded in .ico) ────────────────────────────────
static void GenerateFavicon(string outPath)
{
    // We'll embed two sizes: 16x16 and 32x32
    var sizes = new[] { 16, 32 };
    var pngDataList = new System.Collections.Generic.List<byte[]>();

    FontFamily ff = default;
    foreach (var n in new[] { "Segoe UI", "Arial", "Helvetica" })
        if (SystemFonts.TryGet(n, out ff)) break;
    if (ff.Name is null) ff = SystemFonts.Families.First();

    foreach (var size in sizes)
    {
        var indigo = Color.ParseHex("4f46e5");
        var white  = Color.White;
        float s    = size;

        using var img = new Image<Rgba32>(size, size, Color.Transparent);
        img.Mutate(ctx =>
        {
            // Rounded square background
            ctx.Fill(indigo, new EllipsePolygon(s / 2f, s / 2f, s * 0.48f, s * 0.48f));

            // Eye: outer white ring
            ctx.Fill(white, new EllipsePolygon(s / 2f, s / 2f, s * 0.30f, s * 0.30f));

            // Eye: indigo iris
            ctx.Fill(indigo, new EllipsePolygon(s / 2f, s / 2f, s * 0.17f, s * 0.17f));

            // Eye: white pupil
            ctx.Fill(white, new EllipsePolygon(s / 2f, s / 2f, s * 0.07f, s * 0.07f));
        });

        using var ms = new System.IO.MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        pngDataList.Add(ms.ToArray());
    }

    // Write ICO file (ICONDIR + ICONDIRENTRY[] + PNG data)
    using var ico = new System.IO.MemoryStream();
    using var bw  = new System.IO.BinaryWriter(ico);

    // ICONDIR header
    bw.Write((ushort)0);              // reserved
    bw.Write((ushort)1);              // type: 1 = ICO
    bw.Write((ushort)sizes.Length);   // image count

    // Calculate offsets: header(6) + entries(16 each) + data
    int dataOffset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        var data = pngDataList[i];
        bw.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));  // width
        bw.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));  // height
        bw.Write((byte)0);           // color count
        bw.Write((byte)0);           // reserved
        bw.Write((ushort)1);         // color planes
        bw.Write((ushort)32);        // bits per pixel
        bw.Write((uint)data.Length); // bytes in resource
        bw.Write((uint)dataOffset);  // offset to image data
        dataOffset += data.Length;
    }

    foreach (var data in pngDataList)
        bw.Write(data);

    IOFile.WriteAllBytes(outPath, ico.ToArray());
}

record PostSpec(string Slug, string Title, string Badge, string Category,
                string BgTop, string BgBottom, string Accent, ShapeStyle Shape);

enum ShapeStyle { Circles, Dots, Grid, Versus, Steps }
