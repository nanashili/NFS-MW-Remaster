using UnityEngine;

namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// Small interface-driven shop test surface. It intentionally uses IMGUI
    /// so the shop scene has no dependency on final UI assets, prefabs, or
    /// localization. Every action goes through the public store ports.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleShopTestHarness : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour storeComponent = null!;
        [SerializeField] private MonoBehaviour walletComponent = null!;
        [SerializeField] private MonoBehaviour ownershipComponent = null!;
        [SerializeField] private MonoBehaviour garageComponent = null!;
        [SerializeField] private MonoBehaviour profileComponent = null!;
        [SerializeField] private AssetVehicleStorefront[] storefronts =
            System.Array.Empty<AssetVehicleStorefront>();

        private IVehicleStoreSession store;
        private IVehicleStoreWallet wallet;
        private IVehicleStoreOwnership ownership;
        private IVehicleGarage garage;
        private ICareerProfileService profile;
        private Vector2 productScroll;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle statusStyle;
        private string status = "Choose a storefront to begin.";

        public void Configure(
            MonoBehaviour configuredStore,
            MonoBehaviour configuredWallet,
            MonoBehaviour configuredOwnership,
            MonoBehaviour configuredGarage,
            MonoBehaviour configuredProfile,
            AssetVehicleStorefront[] configuredStorefronts)
        {
            storeComponent = configuredStore;
            walletComponent = configuredWallet;
            ownershipComponent = configuredOwnership;
            garageComponent = configuredGarage;
            profileComponent = configuredProfile;
            storefronts = configuredStorefronts ??
                System.Array.Empty<AssetVehicleStorefront>();
            ResolvePorts();
        }

        private void Awake()
        {
            ResolvePorts();
            if (store != null
                && !store.OpenCategory(VehicleStoreCategory.OneStopShop, out string failure))
            {
                status = failure;
            }
        }

        private void OnGUI()
        {
            ResolvePorts();
            BuildStyles();
            if (store == null)
            {
                DrawUnavailable("No IVehicleStoreSession is attached.");
                return;
            }

            float height = Mathf.Max(260f, Screen.height - 32f);
            GUILayout.BeginArea(new Rect(16f, 16f, 570f, height), GUI.skin.box);
            GUILayout.Label("SHOP TEST SCENE", titleStyle);
            GUILayout.Label(
                "Real storefront navigation and checkout through IVehicleStoreSession",
                bodyStyle);

            DrawStorefrontButtons();
            GUILayout.Space(6f);
            GUILayout.Label(
                string.Format(
                    "ACTIVE: {0}  |  {1}  |  PRODUCTS: {2}",
                    store.ActiveCategory,
                    store.ActiveStoreId,
                    store.VisibleProducts.Count),
                bodyStyle);
            GUILayout.Label(
                string.Format(
                    "CASH: ${0:N0}  |  GARAGE: {1}",
                    wallet == null ? 0 : wallet.Balance,
                    garage == null ? "not attached" : "attached"),
                bodyStyle);

            productScroll = GUILayout.BeginScrollView(
                productScroll,
                GUILayout.Height(Mathf.Max(120f, height - 230f)));
            DrawProducts();
            GUILayout.EndScrollView();

            GUILayout.Label(status, statusStyle);
            DrawProfileButtons();
            GUILayout.EndArea();
        }

        private void DrawStorefrontButtons()
        {
            GUILayout.BeginHorizontal();
            if (storefronts != null)
            {
                for (int i = 0; i < storefronts.Length; i++)
                {
                    IVehicleStorefront storefront = storefronts[i];
                    if (storefront == null)
                    {
                        continue;
                    }

                    if (GUILayout.Button(storefront.DisplayName, GUILayout.Height(30f)))
                    {
                        if (!store.OpenStore(storefront, out string failure))
                        {
                            status = failure;
                        }
                        else
                        {
                            status = "Opened " + storefront.DisplayName + ".";
                        }
                    }
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawProducts()
        {
            for (int i = 0; i < store.VisibleProducts.Count; i++)
            {
                IVehicleStoreProduct product = store.VisibleProducts[i];
                if (product == null)
                {
                    continue;
                }

                bool owned = ownership != null && ownership.IsOwned(product.ProductId);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(
                    string.Format(
                        "{0}\n{1}  |  {2}",
                        product.DisplayName,
                        product.ProductKind,
                        owned ? "OWNED" : "$" + product.Price.ToString("N0")),
                    bodyStyle,
                    GUILayout.MinHeight(42f));
                if (GUILayout.Button(owned ? "OWNED" : "BUY", GUILayout.Width(80f)))
                {
                    if (owned)
                    {
                        status = product.DisplayName + " is already owned.";
                    }
                    else if (!store.TryPurchaseById(product.ProductId, out string failure))
                    {
                        status = failure;
                    }
                    else
                    {
                        status = "Purchased " + product.DisplayName + ".";
                    }
                }

                GUILayout.EndHorizontal();
            }

            if (store.VisibleProducts.Count == 0)
            {
                GUILayout.Label("No products are listed for this storefront.", bodyStyle);
            }
        }

        private void DrawProfileButtons()
        {
            if (profile == null)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SAVE PROFILE"))
            {
                status = profile.TrySave(out string failure)
                    ? "Profile saved."
                    : failure;
            }

            if (GUILayout.Button("LOAD PROFILE"))
            {
                status = profile.TryLoad(out string failure)
                    ? "Profile loaded."
                    : failure;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawUnavailable(string message)
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 570f, 100f), GUI.skin.box);
            GUILayout.Label("SHOP TEST SCENE", titleStyle);
            GUILayout.Label(message, bodyStyle);
            GUILayout.EndArea();
        }

        private void ResolvePorts()
        {
            store = storeComponent as IVehicleStoreSession;
            wallet = walletComponent as IVehicleStoreWallet;
            ownership = ownershipComponent as IVehicleStoreOwnership;
            garage = garageComponent as IVehicleGarage;
            profile = profileComponent as ICareerProfileService;
        }

        private void BuildStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 20;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.white;

            bodyStyle = new GUIStyle(GUI.skin.label);
            bodyStyle.fontSize = 13;
            bodyStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);

            statusStyle = new GUIStyle(bodyStyle);
            statusStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
            statusStyle.wordWrap = true;
        }
    }
}
