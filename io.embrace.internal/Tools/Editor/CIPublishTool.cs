using System;
using System.Collections.Generic;
using System.IO;
using EmbraceSDK.EditorView;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Windows;
using System.Linq;

namespace EmbraceSDK
{
    public static class CIPublishTool
    {
        private const int EXPORT_ERROR_CODE = 109;
        private const string EXPORT_ERROR_MESSAGE = "Exception thrown while exporting package.";

        private const int VERSION_PARSE_ERROR_CODE = 101;
        private const string VERSION_PARSE_ERROR_MESSAGE = "Failed to parse the package version from Packages/io.embrace.sdk/package.json";

        private const int IOS_FRAMEWORK_NOT_FOUND_ERROR_CODE = 121;
        private const string IOS_FRAMEWORK_NOT_FOUND_ERROR_MESSAGE =
            "No instances of Embrace.framework found in project.";
        
        private const string XCFRAMEWORKS_PATH = "Packages/io.embrace.sdk/iOS/xcframeworks/";

        #if DeveloperMode
        /*
         * This segment is only for local developer testing.
         */
        [MenuItem("Embrace/Debug Package Asset Path Names")]
        public static void DebugPackageAssetPathNames()
        {
            // Edit is now meant to only be called in the Publish project
            var guid = AssetDatabase.GUIDFromAssetPath("Packages/io.embrace.sdk/CHANGELOG.md");
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            Debug.Log($"assetPath: {assetPath}");
        }
        
        [MenuItem("Embrace/Export Unity Package")]
        #endif
        public static void ExportUnityPackage()
        {
            // Parse package version
            if (!TryGetPackageVersion(out string packageVersion))
            {
                Debug.LogError(VERSION_PARSE_ERROR_MESSAGE);
                EditorApplication.Exit(VERSION_PARSE_ERROR_CODE);
                return;
            }

            // Export .unitypackage
            Debug.Log("Attempting to export package");
            try
            {
                List<string> exportedPackageAssetList = new List<string>();
                exportedPackageAssetList.Add("Packages/io.embrace.sdk");

                string packageFileName = $"EmbraceSDK_{packageVersion}.unitypackage";

                AssetDatabase.ExportPackage(exportedPackageAssetList.ToArray(), packageFileName, ExportPackageOptions.Recurse);

                Debug.Log("Successfully exported package");
            }
            catch (Exception e)
            {
                Debug.LogError(EXPORT_ERROR_MESSAGE);
                Debug.LogError(e.Message);
                EditorApplication.Exit(EXPORT_ERROR_CODE);
            }
        }

        private static bool TryGetPackageVersion(out string packageVersion)
        {
            TextAsset packageJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/io.embrace.sdk/package.json");

            if (packageJson == null)
            {
                packageVersion = string.Empty;
                return false;
            }

            Package package = JsonUtility.FromJson<Package>(packageJson.text);
            packageVersion = package.version;

            return true;
        }

        /// <summary>
        /// Finds all xcframeworks in the project and sets them to be compatible with iOS and ONLY iOS.
        /// </summary>
        [MenuItem("Embrace/Configure iOS Framework Platform Matrix")]
        public static void ConfigureiOSFrameworkPlatformMatrix()
        {
            /*
             * We cannot depend upon AssetDatabase.FindAssets because its search parameters do not provide an easy way
             * to search for our xcframeworks. Instead, we will search the target location for the xcframeworks and then
             * use this to populate a list of plugins we want to modify.
             *
             * Awkward. Converting from an assetpath to a disk path of a package asset is not straightforward.
             * This is because packages can be in multiple places based on the packages.json import settings.
             * These don't appear to be exposed. So either we process the file ourselves and resolve that way,
             * or we provide a manifest for this tool to read.
             *
             * We will go with the manifest approach. The manifest will have to be generated by the process that creates everything unfortunately.
             * However, we control the location of everything. So if we fix the location of the manifest ... we can find it.
             */
            
            // new approach
            
            // get target directory based on location of asset:
            var manifest_guid = AssetDatabase.FindAssets("xcframework_manifest");
            if (manifest_guid.Length == 0)
            {
                Debug.LogError("Could not find the xcframework manifest.");
                return;
            }
            var manifest_asset_path = AssetDatabase.GUIDToAssetPath(manifest_guid[0]);
            var manifest_asset = AssetDatabase.LoadAssetAtPath<TextAsset>(manifest_asset_path);
            
            Debug.Log($"Found the Embrace symbol upload script at: {manifest_asset_path}");
            
            var framework_paths = manifest_asset.text.Split('\n')
                .Select((path) => Path.Combine(XCFRAMEWORKS_PATH, Path.GetFileNameWithoutExtension(path)))
                .Where((path) => path != XCFRAMEWORKS_PATH);

            var framework_guids = framework_paths.Select((path) => AssetDatabase.FindAssets(Path.GetFileName(path))[0]);
            
            foreach (var guid in framework_guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PluginImporter importer = AssetImporter.GetAtPath(path) as PluginImporter;

                if (importer == null)
                {
                    continue;
                }

                importer.SetCompatibleWithAnyPlatform(false);

                Array allTargets = System.Enum.GetValues(typeof(BuildTarget));
                for (int i = 0; i < allTargets.Length; ++i)
                {
                    BuildTarget target = (BuildTarget)allTargets.GetValue(i);
                    importer.SetExcludeFromAnyPlatform(target, true);
                    importer.SetCompatibleWithPlatform(target, false);
                }
                importer.SetCompatibleWithPlatform(BuildTarget.iOS, true);
                importer.SetCompatibleWithEditor(false);
                importer.SaveAndReimport();
            }
        }
    }
}