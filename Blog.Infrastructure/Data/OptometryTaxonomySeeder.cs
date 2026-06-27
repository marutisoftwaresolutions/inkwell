using Blog.Core.Domain;
using Blog.Core.Interfaces;

namespace Blog.Infrastructure.Data;

/// <summary>
/// Idempotent seeder — runs on every startup. Inserts missing optical-software
/// categories and tags by slug, skipping any that already exist.
/// </summary>
public class OptometryTaxonomySeeder
{
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;

    public OptometryTaxonomySeeder(ICategoryRepository categories, ITagRepository tags)
    {
        _categories = categories;
        _tags = tags;
    }

    public async Task SeedAsync(Guid ownerId)
    {
        await SeedCategoriesAsync(ownerId);
        await SeedTagsAsync(ownerId);
    }

    // ── 10 Topic Cluster Categories ───────────────────────────────────────────

    private async Task SeedCategoriesAsync(Guid ownerId)
    {
        var toSeed = new[]
        {
            ("Practice Management Software",      "practice-management-software"),
            ("EHR & Electronic Records",          "ehr-electronic-records"),
            ("Digital Imaging & Diagnostics",     "digital-imaging-diagnostics"),
            ("AI & Machine Learning in Optometry","ai-machine-learning-optometry"),
            ("Teleoptometry & Remote Care",       "teleoptometry-remote-care"),
            ("Contact Lens Fitting Technology",   "contact-lens-fitting-technology"),
            ("Frame & Lens Design Software",      "frame-lens-design-software"),
            ("Billing & Revenue Cycle",           "billing-revenue-cycle"),
            ("Optical Retail Technology",         "optical-retail-technology"),
            ("Vision Therapy Technology",         "vision-therapy-technology"),
        };

        foreach (var (name, slug) in toSeed)
        {
            var existing = await _categories.GetBySlugAsync(slug, ownerId);
            if (existing != null) continue;

            try
            {
                await _categories.CreateAsync(new Category
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Slug = slug,
                    AuthorId = ownerId
                });
            }
            catch (System.Exception)
            {
                // Safe to ignore unique constraint violations if record exists under another owner
            }
        }
    }

    // ── 60 Keyword-Rich Tags ──────────────────────────────────────────────────

    private async Task SeedTagsAsync(Guid ownerId)
    {
        var toSeed = new[]
        {
            // EHR & practice management products
            ("Eyefinity",                       "eyefinity"),
            ("RevolutionEHR",                   "revolution-ehr"),
            ("Compulink",                       "compulink"),
            ("Crystal PM",                      "crystal-pm"),
            ("MaximEyes",                       "maximeyes"),
            ("OfficeMate",                      "officemate"),
            ("iMedicWare",                      "imedicware"),
            ("My Vision Express",               "my-vision-express"),
            ("OptiMantra",                      "optimantra"),
            ("Uprise EHR",                      "uprise-ehr"),

            // Clinical imaging & diagnostic technology
            ("OCT Software",                    "oct-software"),
            ("Retinal Imaging",                 "retinal-imaging"),
            ("Corneal Topography",              "corneal-topography"),
            ("Wavefront Analysis",              "wavefront-analysis"),
            ("Fundus Photography",              "fundus-photography"),
            ("Aberrometry",                     "aberrometry"),
            ("Digital Slit Lamp",               "digital-slit-lamp"),
            ("Visual Field Software",           "visual-field-software"),
            ("Digital Refraction",              "digital-refraction"),
            ("Autorefractor",                   "autorefractor"),

            // AI & machine learning
            ("AI Diagnostics",                  "ai-diagnostics"),
            ("Deep Learning",                   "deep-learning"),
            ("Computer Vision",                 "computer-vision"),
            ("AI Retinal Screening",            "ai-retinal-screening"),
            ("Glaucoma Detection AI",           "glaucoma-detection-ai"),
            ("Diabetic Retinopathy AI",         "diabetic-retinopathy-ai"),
            ("Predictive Analytics",            "predictive-analytics"),
            ("Clinical Decision Support",       "clinical-decision-support"),

            // Telehealth & remote care
            ("Telemedicine",                    "telemedicine"),
            ("Remote Eye Exam",                 "remote-eye-exam"),
            ("Asynchronous Telehealth",         "asynchronous-telehealth"),
            ("Online Vision Test",              "online-vision-test"),
            ("Patient Portal",                  "patient-portal"),
            ("Virtual Consultation",            "virtual-consultation"),

            // Contact lens fitting technology
            ("Scleral Lens Fitting Software",   "scleral-lens-fitting"),
            ("Digital Keratometry",             "digital-keratometry"),
            ("Orthokeratology Software",        "orthokeratology-software"),
            ("Contact Lens Simulation",         "contact-lens-simulation"),
            ("Topography-Guided Fitting",       "topography-guided-fitting"),

            // Frame & dispensing technology
            ("Virtual Try-On",                  "virtual-try-on"),
            ("3D Frame Visualization",          "3d-frame-visualization"),
            ("Augmented Reality Eyewear",       "augmented-reality-eyewear"),
            ("Frame Inventory Software",        "frame-inventory-software"),
            ("Lens Ordering Software",          "lens-ordering-software"),
            ("Lab Integration",                 "lab-integration"),

            // Billing & compliance
            ("Medical Billing Software",        "medical-billing-software"),
            ("ICD-10 Coding",                   "icd-10-coding"),
            ("Insurance Claims",                "insurance-claims"),
            ("HIPAA Compliance",                "hipaa-compliance"),
            ("Revenue Cycle Management",        "revenue-cycle-management"),
            ("ERA & EOB Processing",            "era-eob-processing"),

            // Retail & point of sale
            ("Optical POS",                     "optical-pos"),
            ("Inventory Management",            "inventory-management"),
            ("Customer Loyalty Software",       "customer-loyalty-software"),
            ("eCommerce for Optometry",         "ecommerce-optometry"),

            // Vision therapy & rehabilitation
            ("Vision Therapy Software",         "vision-therapy-software"),
            ("Binocular Vision Testing",        "binocular-vision-testing"),
            ("Perceptual Learning",             "perceptual-learning"),
            ("Amblyopia Treatment Tech",        "amblyopia-treatment-tech"),

            // Industry & standards
            ("HL7 FHIR",                        "hl7-fhir"),
            ("Interoperability",                "interoperability"),
            ("Cloud-Based EHR",                 "cloud-based-ehr"),
            ("Practice Analytics",              "practice-analytics"),
        };

        foreach (var (name, slug) in toSeed)
        {
            var existing = await _tags.GetBySlugAsync(slug, ownerId);
            if (existing != null) continue;

            try
            {
                await _tags.CreateAsync(new Tag
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Slug = slug,
                    AuthorId = ownerId
                });
            }
            catch (System.Exception)
            {
                // Safe to ignore unique constraint violations if record exists under another owner
            }
        }
    }
}
