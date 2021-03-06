// <copyright file="UnityCompat.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>
using UnityEditor;
using UnityEngine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GooglePlayServices {
// TODO(butterfield): Move to a new assembly, for common use between plugins.

// Provides an API for accessing Unity APIs that work accross various verisons of Unity.
public class UnityCompat {
    private const string Namespace = "GooglePlayServices.";
    private const string ANDROID_MIN_SDK_FALLBACK_KEY = Namespace + "MinSDKVersionFallback";
    private const string ANDROID_PLATFORM_FALLBACK_KEY = Namespace + "PlatformVersionFallback";
    private const string ANDROID_BUILD_TOOLS_FALLBACK_KEY = Namespace + "BuildToolsVersionFallback";
    private const int DEFAULT_ANDROID_MIN_SDK = 14;
    private const int DEFAULT_PLATFORM_VERSION = 25;
    private const string DEFAULT_BUILD_TOOLS_VERSION = "25.0.2";

    private const string UNITY_ANDROID_VERSION_ENUM_PREFIX = "AndroidApiLevel";
    private const string UNITY_ANDROID_EXTENSION_ASSEMBLY = "UnityEditor.Android.Extensions";
    private const string UNITY_ANDROID_SDKTOOLS_CLASS = "UnityEditor.Android.AndroidSDKTools";
    private const string UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS =
        "UnityEditor.Android.PostProcessAndroidPlayer";
    private const string WRITE_A_BUG =
        "Please report this as a bug with the version of Unity you are using at: " +
        "https://github.com/googlesamples/unity-jar-resolver/issues";

    private static int MinSDKVersionFallback {
        get { return EditorPrefs.GetInt(ANDROID_MIN_SDK_FALLBACK_KEY, DEFAULT_ANDROID_MIN_SDK); }
    }
    private static int AndroidPlatformVersionFallback {
        get { return EditorPrefs.GetInt(ANDROID_PLATFORM_FALLBACK_KEY, DEFAULT_PLATFORM_VERSION); }
    }
    private static string AndroidBuildToolsVersionFallback {
        get {
            return EditorPrefs.GetString(ANDROID_BUILD_TOOLS_FALLBACK_KEY,
                                         DEFAULT_BUILD_TOOLS_VERSION);
        }
    }

    // Parses the MinSDKVersion from Unity's Enum of the value, and reports if this gets out of
    // sync from expectations.
    public static int GetAndroidMinSDKVersion() {
        string minSdkVersion = PlayerSettings.Android.minSdkVersion.ToString();
        if (minSdkVersion.StartsWith(UNITY_ANDROID_VERSION_ENUM_PREFIX)) {
            minSdkVersion = minSdkVersion.Substring(UNITY_ANDROID_VERSION_ENUM_PREFIX.Length);
        }
        int versionVal;
        bool validVersionString = Int32.TryParse(minSdkVersion, out versionVal);
        if (!validVersionString) {
            Debug.LogError("Could not determine the Android Min SDK Version from the Unity " +
                           "version enum. Resorting to reading a fallback value from the editor " +
                           "preferences " + ANDROID_MIN_SDK_FALLBACK_KEY + ": " +
                           MinSDKVersionFallback.ToString() + ". " +
                           WRITE_A_BUG);
        }
        return validVersionString ? versionVal : MinSDKVersionFallback;
    }

    private static Type AndroidSDKToolsClass {
        get {
            Type sdkTools = Type.GetType(
                UNITY_ANDROID_SDKTOOLS_CLASS + ", " + UNITY_ANDROID_EXTENSION_ASSEMBLY);
            if (sdkTools == null) {
                Debug.LogError("Could not find the " +
                    UNITY_ANDROID_SDKTOOLS_CLASS + " class via reflection. " + WRITE_A_BUG);
                return null;
            }
            return sdkTools;
        }
    }

    private static object SDKToolsInst {
        get {
            MethodInfo getInstanceMethod = null;
            Type sdkClass = AndroidSDKToolsClass;
            if (sdkClass != null)
                getInstanceMethod = sdkClass.GetMethod("GetInstance");
            if (getInstanceMethod == null) {
                Debug.LogError("Could not find the " +
                    "AndroidSDKToolsClass.GetInstance method via reflection. " + WRITE_A_BUG);
                return null;
            }

            return getInstanceMethod.Invoke(null, BindingFlags.NonPublic | BindingFlags.Static,
                                            null, new object[] {}, null);
        }
    }

    private static Type PostProcessAndroidPlayerClass {
        get {
            Type sdkTools = Type.GetType(
                UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS + ", " +
                UNITY_ANDROID_EXTENSION_ASSEMBLY);
            if (sdkTools == null) {
                Debug.LogError("Could not find the " +
                    UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS +
                    " class via reflection. " + WRITE_A_BUG);
                return null;
            }
            return sdkTools;
        }
    }

    private static object PostProcessAndroidPlayerInst {
        get {
            Type androidPlayerClass = PostProcessAndroidPlayerClass;
            ConstructorInfo constructor = null;
            if (androidPlayerClass != null)
                constructor = androidPlayerClass.GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] {}, null);
            if (constructor == null) {
                Debug.LogError("Could not find the " +
                    UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS +
                    " constructor via reflection. " + WRITE_A_BUG);
                return null;
            }

            return constructor.Invoke(null);
        }
    }

    // Gets the latest SDK version that's currently installed.
    // This is required for generating gradle builds.
    public static int GetAndroidPlatform() {
        MethodInfo platformVersionMethod = null;
        Type sdkClass = AndroidSDKToolsClass;
        if (sdkClass != null) {
            platformVersionMethod = sdkClass.GetMethod("GetTopAndroidPlatformAvailable");
            if (platformVersionMethod != null) {
                return (int)platformVersionMethod.Invoke(SDKToolsInst,
                    BindingFlags.NonPublic, null, new object[] { null }, null);
            }
        }

        // In Unity 4.x the method to get the platform was different and complex enough that
        // another function was was made to get the value unfortunately on another class.
        sdkClass = PostProcessAndroidPlayerClass;
        if (sdkClass != null) {
            platformVersionMethod = sdkClass.GetMethod("GetAndroidPlatform",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (platformVersionMethod != null) {
                object inst = PostProcessAndroidPlayerInst;
                return (int)platformVersionMethod.Invoke(inst,
                    BindingFlags.NonPublic, null, new object[] {}, null);
            }
        }

        Debug.LogError(String.Format(
            "Could not find the {0}.GetTopAndroidPlatformAvailable or " +
            "{1}.GetAndroidPlatform methods via reflection. {2} Resorting to reading a fallback " +
            "value from the editor preferences {3}: {4}",
            UNITY_ANDROID_SDKTOOLS_CLASS, UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS,
            WRITE_A_BUG, ANDROID_PLATFORM_FALLBACK_KEY, AndroidPlatformVersionFallback));

        return AndroidPlatformVersionFallback;
    }

    // Finds the latest build tools version that's currently installed.
    // This is required for generating gradle builds.
    public static string GetAndroidBuildToolsVersion() {
        // TODO: We're using reflection to access Unity's SDK helpers to get at this info
        // but since this is likely fragile, and may not even be consistent accross versions
        // of Unity, we'll need to replace it with our own.
        MethodInfo buildToolsVersionMethod = null;
        Type sdkClass = AndroidSDKToolsClass;
        if (sdkClass != null)
            buildToolsVersionMethod = sdkClass.GetMethod("BuildToolsVersion");
        if (buildToolsVersionMethod != null) {
            return (string)buildToolsVersionMethod.Invoke(SDKToolsInst,
                BindingFlags.NonPublic, null, new object[] { null }, null);
        }

        Debug.LogError("Could not find the " + UNITY_ANDROID_SDKTOOLS_CLASS +
            ".BuildToolsVersion method via reflection. " + WRITE_A_BUG +
            " Resorting to reading a fallback value from the editor preferences " +
            ANDROID_BUILD_TOOLS_FALLBACK_KEY + ": " + AndroidBuildToolsVersionFallback);
        return AndroidBuildToolsVersionFallback;
    }

    /// <summary>
    /// Get the application / bundle ID property.
    /// </summary>
    /// <returns>Application / bundle ID property or null if it can't be found.</returns>
    private static PropertyInfo GetApplicationIdProperty() {
        var playerSettingsType = typeof(UnityEditor.PlayerSettings);
        var property = playerSettingsType.GetProperty("applicationIdentifier");
        property = property ?? playerSettingsType.GetProperty("bundleIdentifier");
        if (property == null) {
            Debug.LogWarning("Unable to retrieve PlayerSettings.applicationIdentifier property");
        }
        return property;
    }

    /// <summary>
    /// Get / set the bundle / application ID.
    /// </summary>
    /// This uses reflection to retrieve the property as it was renamed in Unity 5.6.
    public static string ApplicationId {
        get {
            var property = GetApplicationIdProperty();
            return property != null ? (string)property.GetValue(null, null) : null;
        }

        set {
            var property = GetApplicationIdProperty();
            if (property != null) property.SetValue(null, value, null);
        }
    }
}
}
