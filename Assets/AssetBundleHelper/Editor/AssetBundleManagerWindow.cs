using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;

public class AssetBundleManagerWindow : EditorWindow {
	
	public List<FileInfo> detectedBundlesFileInfos;
	public List<AssetBundleListing> detectedBundles;
	public Vector2 bundleListScrollPos;
	
	[MenuItem("Window/AssetBundleManager %&5")]
	public static void Initialize(){
		AssetBundleManagerWindow window = EditorWindow.GetWindow(typeof(AssetBundleManagerWindow)) as AssetBundleManagerWindow;
		window.Refresh();
	}

	
	public void Refresh(){
		//Search for existing AssetBundleListings
		detectedBundlesFileInfos = new List<FileInfo>();
		detectedBundles = new List<AssetBundleListing>();
		DirectoryInfo di = new DirectoryInfo(Application.dataPath); //Assets directory
		FileInfo[] files = di.GetFiles("*.asset", SearchOption.AllDirectories);
		foreach(FileInfo fi in files){
			string projectRelativePath = fi.FullName.Substring(di.Parent.FullName.Length + 1); //+1 includes slash
			AssetBundleListing abl = AssetDatabase.LoadAssetAtPath(projectRelativePath, typeof(AssetBundleListing)) as AssetBundleListing;
			if(abl != null){
				detectedBundlesFileInfos.Add(fi);
				detectedBundles.Add(abl);
			}
		}
		Repaint();
	}

	public void OnGUI(){
		if(detectedBundlesFileInfos == null || detectedBundles == null){
			Refresh();
			return;
		}
		EditorGUIUtility.LookLikeControls();
		//Layout changes during a refresh which makes mousedown event throw an exception.
		//Delaying refresh to the Repaint stage causes the window to flicker,
		//so just consume the exception and stop trying to parse mouse input this frame
		try{ 
			GUILayout.BeginHorizontal();
		}
		catch(ArgumentException){
			Event.current.type = EventType.used;
			return;
		}
		GUILayout.Label("Bundles", EditorStyles.boldLabel);
		GUILayout.FlexibleSpace();
		if(GUILayout.Button("Refresh",EditorStyles.miniButton)){
			Refresh();
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.BeginHorizontal(EditorStyles.toolbar);
		GUILayout.Label("Name", GUILayout.MinWidth(100));
		GUILayout.FlexibleSpace();
		GUILayout.Label("Variants");
		GUILayout.EndHorizontal();
		
		bundleListScrollPos = GUILayout.BeginScrollView(bundleListScrollPos);
		for(int i = 0; i < detectedBundles.Count; i++){
			AssetBundleListing listing = detectedBundles[i];
			if(listing == null){
				Refresh();
				return;
			}
			FileInfo listingFile = detectedBundlesFileInfos[i];
			if(listingFile == null){
				Refresh();
				return;
			}			
			GUILayout.BeginHorizontal();
			if(GUILayout.Button(listing.name,EditorStyles.miniButton,GUILayout.MinWidth(100))){
				Selection.activeObject = listing;
				EditorGUIUtility.PingObject(Selection.activeObject);
			}
			GUILayout.FlexibleSpace();
			GUILayout.Label(AssetBundleListingEditor.Settings.MaskToTagString(detectedBundles[i].tagMask));
			GUILayout.EndHorizontal();
		}
		GUILayout.EndScrollView();
		GUILayout.EndVertical();

		if(GUILayout.Button("Build AssetBundles for all platforms", GUILayout.Height(48))){
			foreach(var platform in AssetBundleListingEditor.Settings.platforms){
				BuildBundlesForPlatform(platform);
			}			
		}
		foreach(var platform in AssetBundleListingEditor.Settings.platforms){
			if(GUILayout.Button("Build AssetBundles for " + platform.name)){
				BuildBundlesForPlatform(platform);
			}
		}
	}
	
	private void BuildBundlesForPlatform(BundlePlatform platform){
		//Construct base build map
		AssetBundleBuild[] baseBuild = new AssetBundleBuild[detectedBundles.Count];
		for(int i = 0; i < detectedBundles.Count; i++){
			List<BundleTagGroup> tagGroups = detectedBundles[i].ActiveTagGroupsForcePlatformGroup;
			List<BundleTag> defaultNonPlatformTags = BundleTagUtils.DefaultTagCombination(tagGroups, 1); //1 = skip platform group
			
			var defaultTagsIncPlatform = ((BundleTag)platform).Yield().Concat(defaultNonPlatformTags);
			string defaultTagStringIncPlatform = BundleTagUtils.BuildTagString(defaultTagsIncPlatform);
			string defaultTagString = BundleTagUtils.BuildTagString(BundleTagUtils.DefaultTagCombination(detectedBundles[i].ActiveTagGroups, 0));
			
			baseBuild[i].assetBundleName = detectedBundles[i].name + "_" + defaultTagStringIncPlatform;					 
			baseBuild[i].assetNames = detectedBundles[i].GetAssetsForTags(defaultTagString).Select(x => AssetDatabase.GetAssetPath(x)).ToArray();
		}
		BuildPipeline.BuildAssetBundles("Bundles/", baseBuild, BuildAssetBundleOptions.None, platform.unityBuildTarget);
	}
	
	public DateTime GetLastWriteTime(AssetBundleListing listing, string platform){
		string path = AssetBundleListingEditor.Settings.bundleDirectoryRelativeToProjectFolder
		+ Path.DirectorySeparatorChar + listing.name + "_" + platform+".unity3d";
		var fileInfo = new FileInfo(path);
		if(!fileInfo.Exists){
			return new DateTime((System.Int64)0);
		}
		return fileInfo.LastWriteTimeUtc;
	}
}
