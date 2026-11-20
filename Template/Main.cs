using System.Reflection;
using Alta.Networking;
using MelonLoader;
using SyncLib.Items;
using UnityEngine;

// Name + Author form SyncLib's ModId ("Template.you"). Changing either after release orphans the saved hashes.
[assembly: MelonInfo(typeof(Template.Main), "Template", "0.1.0", "you")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace Template {
    public class Main : MelonMod {
        public override void OnInitializeMelon() {
            // Registration is only legal inside this event (SyncLib rejects calls made at any other time).
            NetworkPrefabRegistry.RegisterPrefabs += RegisterMyPrefabs;
        }

        private void RegisterMyPrefabs() {
            // Bundle file dropped in Resources\ -> "<AssemblyName>.Resources.<file name>"
            GameObject? obj = LoadPrefab("Template.Resources.example", "Example Item");
            if (obj == null) return;

            // Null = error (logged), or we're a client and the server doesn't have this mod.
            NetworkPrefab? prefab = this.RegisterCustomPrefab(obj, "ExampleItem");
            if (prefab == null) return;

            // Only if the prefab was built in the ATT Workshop Unity project with an Item component.
            prefab.RegisterCustomItem();
        }

        private GameObject? LoadPrefab(string resource, string assetName) {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream == null) {
                LoggerInstance.Error($"Embedded resource '{resource}' not found.");
                return null;
            }
            return AssetBundle.LoadFromStream(stream).LoadAsset<GameObject>(assetName);
        }
    }
}
