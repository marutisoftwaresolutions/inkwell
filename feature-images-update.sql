-- =================================================================
-- feature-images-update.sql
-- Updates FeatureImage for all 10 Week-1 EHR posts.
-- Run AFTER FeatureImageGenerator has run and populated
-- Blog.Web/wwwroot/uploads/features/
-- =================================================================

UPDATE Posts SET FeatureImage = N'/uploads/features/best-optometry-ehr-software-2026.jpg'  WHERE Slug = N'best-optometry-ehr-software-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/eyefinity-ehr-review.jpg'  WHERE Slug = N'eyefinity-ehr-review';
UPDATE Posts SET FeatureImage = N'/uploads/features/revolution-ehr-review.jpg'  WHERE Slug = N'revolution-ehr-review';
UPDATE Posts SET FeatureImage = N'/uploads/features/compulink-optometry-review.jpg'  WHERE Slug = N'compulink-optometry-review';
UPDATE Posts SET FeatureImage = N'/uploads/features/crystal-pm-vs-officemate.jpg'  WHERE Slug = N'crystal-pm-vs-officemate';
UPDATE Posts SET FeatureImage = N'/uploads/features/maximeyes-ehr-review.jpg'  WHERE Slug = N'maximeyes-ehr-review';
UPDATE Posts SET FeatureImage = N'/uploads/features/imedicware-optometry-review.jpg'  WHERE Slug = N'imedicware-optometry-review';
UPDATE Posts SET FeatureImage = N'/uploads/features/cloud-vs-server-optometry-ehr.jpg'  WHERE Slug = N'cloud-vs-server-optometry-ehr';
UPDATE Posts SET FeatureImage = N'/uploads/features/how-to-switch-optometry-ehr.jpg'  WHERE Slug = N'how-to-switch-optometry-ehr';
UPDATE Posts SET FeatureImage = N'/uploads/features/optometry-ehr-implementation-checklist.jpg'  WHERE Slug = N'optometry-ehr-implementation-checklist';
UPDATE Posts SET FeatureImage = N'/uploads/features/hipaa-compliant-ehr-optometrists-2026.jpg'  WHERE Slug = N'hipaa-compliant-ehr-optometrists-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/best-optometry-practice-management-software-2026.jpg'  WHERE Slug = N'best-optometry-practice-management-software-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/optometry-billing-software-reduce-claim-denials-2026.jpg'  WHERE Slug = N'optometry-billing-software-reduce-claim-denials-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/best-contact-lens-fitting-software-optometrists-2026.jpg'  WHERE Slug = N'best-contact-lens-fitting-software-optometrists-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/optical-retail-software-increase-frame-sales.jpg'  WHERE Slug = N'optical-retail-software-increase-frame-sales';
UPDATE Posts SET FeatureImage = N'/uploads/features/teleoptometry-platforms-compared-2026.jpg'  WHERE Slug = N'teleoptometry-platforms-compared-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/frame-lens-inventory-management-software-optical-dispensaries.jpg'  WHERE Slug = N'frame-lens-inventory-management-software-optical-dispensaries';
UPDATE Posts SET FeatureImage = N'/uploads/features/ai-powered-optometry-machine-learning-eye-care-2026.jpg'  WHERE Slug = N'ai-powered-optometry-machine-learning-eye-care-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/best-digital-retinal-cameras-optometry-2026.jpg'  WHERE Slug = N'best-digital-retinal-cameras-optometry-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/vision-therapy-software-management-tools-ods-2026.jpg'  WHERE Slug = N'vision-therapy-software-management-tools-ods-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/officemate-ehr-review-2026.jpg'  WHERE Slug = N'officemate-ehr-review-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/patient-communication-tools-optometry-recalls-reminders.jpg'  WHERE Slug = N'patient-communication-tools-optometry-recalls-reminders';
UPDATE Posts SET FeatureImage = N'/uploads/features/icd-10-cpt-codes-optometry-2026-reference-guide.jpg'  WHERE Slug = N'icd-10-cpt-codes-optometry-2026-reference-guide';
UPDATE Posts SET FeatureImage = N'/uploads/features/contact-lens-practice-builder-software-grow-revenue.jpg'  WHERE Slug = N'contact-lens-practice-builder-software-grow-revenue';
UPDATE Posts SET FeatureImage = N'/uploads/features/optical-pos-systems-eye-care-stores-2026.jpg'  WHERE Slug = N'optical-pos-systems-eye-care-stores-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/teleoptometry-reimbursement-guide-state-by-state.jpg'  WHERE Slug = N'teleoptometry-reimbursement-guide-state-by-state';
UPDATE Posts SET FeatureImage = N'/uploads/features/anti-reflective-lens-coating-track-upsell-optometry-software.jpg'  WHERE Slug = N'anti-reflective-lens-coating-track-upsell-optometry-software';
UPDATE Posts SET FeatureImage = N'/uploads/features/glaucoma-detection-software-ai-tools-optometrists-2026.jpg'  WHERE Slug = N'glaucoma-detection-software-ai-tools-optometrists-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/oct-vs-fundus-photography-optometry.jpg'  WHERE Slug = N'oct-vs-fundus-photography-optometry';
UPDATE Posts SET FeatureImage = N'/uploads/features/treating-amblyopia-children-software-track-progress-2026.jpg'  WHERE Slug = N'treating-amblyopia-children-software-track-progress-2026';
UPDATE Posts SET FeatureImage = N'/uploads/features/ehr-interoperability-optometrists-connecting-medical-systems.jpg'  WHERE Slug = N'ehr-interoperability-optometrists-connecting-medical-systems';
UPDATE Posts SET FeatureImage = N'/uploads/features/optometry-practice-analytics-grow-eye-care-business.jpg'  WHERE Slug = N'optometry-practice-analytics-grow-eye-care-business';
UPDATE Posts SET FeatureImage = N'/uploads/features/medical-vs-vision-insurance-billing-optometry-guide.jpg'  WHERE Slug = N'medical-vs-vision-insurance-billing-optometry-guide';
UPDATE Posts SET FeatureImage = N'/uploads/features/contact-lens-inventory-management-software-optometry.jpg'  WHERE Slug = N'contact-lens-inventory-management-software-optometry';
UPDATE Posts SET FeatureImage = N'/uploads/features/diabetic-eye-exam-ai-screening-tools-optometrists-2026.jpg'  WHERE Slug = N'diabetic-eye-exam-ai-screening-tools-optometrists-2026';

-- Verify
SELECT Slug, FeatureImage FROM Posts WHERE FeatureImage LIKE '/uploads/features/%' ORDER BY Slug;
