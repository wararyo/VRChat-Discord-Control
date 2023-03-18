using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class SettingsProvider : MonoBehaviour
{
    [Serializable]
    public struct Settings
    {
        public string clientId;
        public string clientSecret;
        public string accessToken;
    }

    public static Settings settings;

    const string FILE_NAME = "settings.json";

#if UNITY_EDITOR
    string settingsFilePath = $"Assets/{FILE_NAME}";
#elif UNITY_STANDALONE_WIN
    string settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), FILE_NAME);
#endif

    void Awake()
    {
        Load();
    }

    void Load()
    {
        if (!File.Exists(settingsFilePath))
        {
            Debug.Log("The settings file was not found.");
            return;
        }

        try
        {
            using (StreamReader sr = new StreamReader(settingsFilePath))
            {
                settings = JsonConvert.DeserializeObject<Settings>(sr.ReadToEnd());
            }
            Debug.Log("Settings loaded successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError("Loading settings failed: " + ex.Message);
        }
    }

    void Save()
    {
        try
        {
            using (StreamWriter sw = new StreamWriter(settingsFilePath))
            {
                string json = JsonConvert.SerializeObject(settings,Formatting.Indented);
                sw.Write(json);
            }
            Debug.Log("Settings saved successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError("Saving settings failed: " + ex.Message);
        }
    }
}
