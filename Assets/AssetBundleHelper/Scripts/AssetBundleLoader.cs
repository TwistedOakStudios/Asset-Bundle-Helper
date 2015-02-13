﻿using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using System.IO;
/* 	Workhorse class that loads/unloads the correct backing AssetBundles for AssetBundleListings,
	and also fetches assets from within those bundles. Handles loading/unloading known dependencies automatically.
	Uses reference counting to know what it can unload, so be sure to match Get calls with corresponding
	Release calls. */
public static class AssetBundleLoader {

	//Returns the path to the directory where all AssetBundles are loaded from
	public static string BasePath{
		get{
			return basePathProvider.GetPath();
		}
	}
	//Source of BasePath. Can be replaced with an extension of AssetBundlePathProvider if necessary.
	public static AssetBundlePathProvider basePathProvider = new AssetBundlePathProvider();
	
	//Reference-counting cache of loaded AssetBundles
	private static DictionaryCache<AssetBundle> bundleCache = new DictionaryCache<AssetBundle>();

	//Object for interfacing with some Unity-isms, particularly running Coroutines.
	//Configured to not get destroyed during scene transitions.
	private static AssetBundleLoaderHelper LoaderHelper{
		get{
			if(!loaderHelper){
#if UNITY_EDITOR
				//Find pre-existing if possible so we don't leak objects with every recompile
				var obj = GameObject.Find("/__AssetBundleLoader");
				if(obj){
					loaderHelper = obj.GetComponent<AssetBundleLoaderHelper>();
				}
				if(!loaderHelper){
					loaderHelper = EditorUtility.CreateGameObjectWithHideFlags("__AssetBundleLoader", HideFlags.HideAndDontSave, typeof(AssetBundleLoaderHelper)).GetComponent<AssetBundleLoaderHelper>();
				}
#else
				loaderHelper = new GameObject("__AssetBundleLoader", typeof(AssetBundleLoaderHelper)).GetComponent<AssetBundleLoaderHelper>();
				loaderHelper.gameObject.hideFlags = HideFlags.HideAndDontSave;
#endif
			}
			return loaderHelper;
		}
	}
	private static AssetBundleLoaderHelper loaderHelper;
	
	//Returns the path (directory+filename) to an AssetBundle.
	public static string AssetBundlePath(string assetBundleFileName){
		return Path.Combine(BasePath, assetBundleFileName);
	}
	
	//Asyncronously fetches an asset from the given listing with the given name.
	//If fetching several assets it is faster to use GetBundle() and then bundle.LoadAsset().
	//Optionally allows the use of a particular tag set instead of the default, currently active set
	public static Coroutine<T> GetAsset<T>(AssetBundleListing listing, string assetName, BundleTagSelection targetTags = null){
		//Default to currently active tag set
		if(targetTags == null){
			targetTags = AssetBundleRuntimeSettings.ActiveTags;
		}
		return LoaderHelper.StartCoroutine<T>(GetAssetCoroutine(listing, assetName, targetTags));
	}
	
	//Asyncronously fetches the asset bundle corresponding to the given listing
	//Optionally allows the use of a particular tag set instead of the default, currently active set
	public static Coroutine<AssetBundle> GetBundle(AssetBundleListing listing, BundleTagSelection targetTags = null){
		//Default to currently active tag set
		if(targetTags == null){
			targetTags = AssetBundleRuntimeSettings.ActiveTags;
		}
		return LoaderHelper.StartCoroutine<AssetBundle>(GetBundleCoroutine(listing, targetTags));
	}
	
	//Releases the asset with the given name, from the given listing.
	//Note that this does not necessarily unload the asset from memory.
	//Currently, individual asset references aren't tracked, so this exists just to
	//properly mirror GetAsset() in the event that someday we do.
	public static void ReleaseAsset(AssetBundleListing listing, string assetName){
		ReleaseBundle(listing);		
	}
	
	//Releases the AssetBundle corresponding to the given AssetBundleListing.
	//Note that this does not necessarily unload the AssetBundle from memory.
	//Optionally allows forcing the target tag selection instead of using the default, currently active tag set
	public static void ReleaseBundle(AssetBundleListing listing, BundleTagSelection targetTags = null){
		if(targetTags == null){
			targetTags = AssetBundleRuntimeSettings.ActiveTags;
		}
		string key = GetKey(listing, targetTags);
		if(!bundleCache.ContainsKey(key)){
			Debug.LogError("No bundle with id " + key);
			return;
		}
		var bundle = bundleCache.GetUntracked(key);
		if(bundleCache.Release(key)){
			bundle.Unload(false);
		}
		foreach(AssetBundleListing dependency in listing.dependencies){
			ReleaseBundle(dependency);
		}
	}
	
	//Gets the bundle cache key for a given bundle listing and the given tag selection.
	private static string GetKey(AssetBundleListing listing, BundleTagSelection tags){
		if((tags.Mask & listing.tagMask) != listing.tagMask){
			throw new System.ArgumentException("Cannot construct key: Tags selection does not include tags required by listing", "tags");
		}
		return listing.FileNamePrefix + AssetBundleChars.BundleSeparator + tags.GetMasked(listing.tagMask).ToString();
	}
	
	//Workhorse function for GetAsset()
	private static IEnumerator GetAssetCoroutine(AssetBundleListing listing, string assetName, BundleTagSelection targetTags){
		//Get AssetBundle
		Coroutine<AssetBundle> bundleCoroutine = GetBundle(listing, targetTags);
		yield return bundleCoroutine.coroutine;
		AssetBundle bundle = bundleCoroutine.Value;
		//Load asset from bundle
		if(bundle.Contains(assetName)){
			AssetBundleRequest req = bundle.LoadAssetAsync(assetName);
			yield return req; //Wait for load to finish
			yield return req.asset;
		}
		else{
			Debug.LogError("Bundle " + listing + "does not contain " + assetName);
			yield break;
		}
	}
	
	//Workhorse function for GetBundle()
	private static IEnumerator GetBundleCoroutine(AssetBundleListing listing, BundleTagSelection targetTags){
		//Ensure dependencies are already loaded first
		foreach(AssetBundleListing dependency in listing.dependencies){
			yield return GetBundle(dependency, targetTags).coroutine;
		}
		//Load AssetBundle if not cached
		string key = GetKey(listing, targetTags);
		if(!bundleCache.ContainsKey(key)){
			Debug.Log("Load AssetBundle from " + AssetBundlePath(listing.FileName(targetTags)));
			var www = new WWW(AssetBundlePath(key));
			yield return www;
			var wwwBundle = www.assetBundle;
			if(wwwBundle){
				bundleCache.Add(key, wwwBundle);
			}
			www.Dispose();
		}
		//Return cached bundle
		var bundle = bundleCache.Get(key);
		yield return bundle;
	}
}
