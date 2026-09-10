using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor
{
    /// <summary>Editor-only reference catalogue. IDs, not labels, identify records and saved vehicles.</summary>
    [CreateAssetMenu(menuName = "NFS MW Remaster/Vehicle Profiles/Identity Catalogue")]
    public sealed class VehicleIdentityCatalog : ScriptableObject
    {
        public const int CurrentSchema = 1;
        [SerializeField, HideInInspector] private int schema = CurrentSchema;
        [SerializeField] private List<VehicleBrandRecord> brands = new List<VehicleBrandRecord>();
        [SerializeField] private List<VehicleModelRecord> models = new List<VehicleModelRecord>();
        public int Schema => schema;
        public IReadOnlyList<VehicleBrandRecord> Brands => brands;
        public IReadOnlyList<VehicleModelRecord> Models => models;

        public VehicleBrandRecord FindBrand(string id) => brands.Find(x => x != null && x.Id == id);
        public VehicleModelRecord FindModel(string id) => models.Find(x => x != null && x.Id == id);

        public VehicleBrandRecord AddBrand(string label)
        {
            RequireLabel(label);
            var record = new VehicleBrandRecord { displayName = label.Trim() };
            brands.Add(record);
            return record;
        }

        public VehicleModelRecord AddModel(string brandId, string label, string generation)
        {
            if (FindBrand(brandId) == null) throw new ArgumentException("Select an existing manufacturer.");
            RequireLabel(label);
            var record = new VehicleModelRecord { brandId = brandId, displayName = label.Trim(), generation = generation ?? "" };
            models.Add(record);
            return record;
        }

        public void AddYear(string modelId, int year)
        {
            var model = FindModel(modelId) ?? throw new ArgumentException("Select an existing model.");
            if (year < 1886 || year > 9999) throw new ArgumentOutOfRangeException(nameof(year));
            if (model.years.Contains(year)) throw new ArgumentException("This model year already exists.");
            model.years.Add(year);
            model.years.Sort();
        }

        public VehicleVariantRecord AddVariant(string modelId, int year, string label)
        {
            var model = FindModel(modelId) ?? throw new ArgumentException("Select an existing model.");
            if (!model.years.Contains(year)) throw new ArgumentException("Add the model year first.");
            RequireLabel(label);
            var record = new VehicleVariantRecord { year = year, displayName = label.Trim() };
            model.variants.Add(record);
            return record;
        }

        /// <summary>Deletion is deliberately fail-closed when a caller supplies a referenced identity.</summary>
        public void RemoveVariant(string modelId, string variantId, IEnumerable<VehicleProfileDraft> profiles)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            if (profiles.Any(p => p != null && p.catalog == this && p.variantId == variantId))
                throw new InvalidOperationException("A profile still references this variant.");
            var model = FindModel(modelId) ?? throw new ArgumentException("Unknown model.");
            model.variants.RemoveAll(x => x.Id == variantId);
        }

        public string[] ValidateRecords()
        {
            var failures = new List<string>();
            if (schema != CurrentSchema) failures.Add("Unsupported catalogue schema; no implicit migration is permitted.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var brand in brands)
            {
                if (brand == null) { failures.Add("Null manufacturer record."); continue; }
                CheckIdentity(brand.Id, brand.displayName, ids, failures);
            }
            foreach (var model in models)
            {
                if (model == null) { failures.Add("Null model record."); continue; }
                CheckIdentity(model.Id, model.displayName, ids, failures);
                if (FindBrand(model.brandId) == null) failures.Add("Model has an unresolved manufacturer: " + model.displayName);
                if (model.years.Distinct().Count() != model.years.Count || model.years.Any(y => y < 1886 || y > 9999))
                    failures.Add("Invalid or duplicate model years: " + model.displayName);
                foreach (var variant in model.variants)
                {
                    if (variant == null) { failures.Add("Null variant record."); continue; }
                    CheckIdentity(variant.Id, variant.displayName, ids, failures);
                    if (!model.years.Contains(variant.year)) failures.Add("Variant references an undeclared model year.");
                }
                if ((model.productionStartYear != 0 || model.productionEndYear != 0) &&
                    (model.productionStartYear < 1886 || model.productionEndYear < model.productionStartYear || model.productionEndYear > 9999))
                    failures.Add("Production-year range must be unknown (both zero) or a valid authored range.");
            }
            return failures.ToArray();
        }

        private static void CheckIdentity(string id, string label, HashSet<string> ids, List<string> failures)
        {
            if (!Guid.TryParseExact(id, "N", out _) || !ids.Add(id)) failures.Add("Missing, invalid or duplicate immutable identity.");
            if (string.IsNullOrWhiteSpace(label)) failures.Add("A display name is required.");
        }

        private static void RequireLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Enter a display name.");
        }
    }

    [Serializable]
    public sealed class VehicleBrandRecord
    {
        [SerializeField, HideInInspector] private string id = Guid.NewGuid().ToString("N");
        public string Id => id;
        public string displayName = "";
        public string[] aliases = Array.Empty<string>();
        public Texture2D logo;
        [TextArea] public string provenance = "";
    }

    [Serializable]
    public sealed class VehicleModelRecord
    {
        [SerializeField, HideInInspector] private string id = Guid.NewGuid().ToString("N");
        public string Id => id;
        [HideInInspector] public string brandId = "";
        public string displayName = "", generation = "";
        [Tooltip("Authored reference information; zero means unknown. Not the selectable model year.")]
        public int productionStartYear, productionEndYear;
        public List<int> years = new List<int>();
        public List<VehicleVariantRecord> variants = new List<VehicleVariantRecord>();
    }

    [Serializable]
    public sealed class VehicleVariantRecord
    {
        [SerializeField, HideInInspector] private string id = Guid.NewGuid().ToString("N");
        public string Id => id;
        public int year;
        public string displayName = "";
    }
}
