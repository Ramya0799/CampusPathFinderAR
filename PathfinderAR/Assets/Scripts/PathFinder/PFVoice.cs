// PathFinder – spoken turn-by-turn guidance using Android's built-in TextToSpeech (no plugin or package needed).
// On Android it speaks; in the Editor and on other platforms it only logs the sentence to the Console.
// Usage (from PFCampusNavigator):  voice = PFVoice.Get(gameObject);  voice.Say("Turn left, then go 12 m");
using UnityEngine;

namespace PathFinderNav
{
    public class PFVoice : MonoBehaviour
    {
        [Tooltip("Speak the directions aloud. The user can switch it with the VOICE button; the choice is remembered.")]
        public bool voiceOn = true;
        [Tooltip("Language tag for the phone's text-to-speech voice, e.g. en-IN, en-US, hi-IN, kn-IN.")]
        public string language = "en-IN";
        [Range(0.5f, 2f)] public float speechRate = 1.0f;
        [Tooltip("The same sentence is not repeated within this many seconds.")]
        public float repeatGuardSeconds = 4f;

        const string PrefKey = "pf_voice_on";
        string lastSpoken = "";
        float lastTime = -100f;

        public static PFVoice Get(GameObject host)
        {
            var v = host.GetComponent<PFVoice>();
            if (v == null) v = host.AddComponent<PFVoice>();
            return v;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject tts;
        bool ready, initHandled;
        volatile int initStatus = int.MinValue;   // set from the Java thread, handled on the Unity main thread
        string pending;
        int utterance;

        class InitListener : AndroidJavaProxy
        {
            readonly PFVoice owner;
            public InitListener(PFVoice o) : base("android.speech.tts.TextToSpeech$OnInitListener") { owner = o; }
            public void onInit(int status) { owner.initStatus = status; }
        }
#endif

        void Awake()
        {
            try { voiceOn = PlayerPrefs.GetInt(PrefKey, voiceOn ? 1 : 0) == 1; } catch (System.Exception) { }
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener(this));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PathFinder] Voice guidance not available: " + e.Message);
                tts = null;
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void Update()
        {
            if (initHandled || initStatus == int.MinValue || tts == null) return;
            initHandled = true;
            if (initStatus != 0)   // TextToSpeech.SUCCESS == 0
            {
                Debug.LogWarning("[PathFinder] Text-to-speech failed to start (status " + initStatus + ").");
                return;
            }
            try
            {
                using (var localeClass = new AndroidJavaClass("java.util.Locale"))
                using (var locale = localeClass.CallStatic<AndroidJavaObject>("forLanguageTag", language))
                {
                    int r = tts.Call<int>("setLanguage", locale);
                    if (r < 0)   // LANG_MISSING_DATA (-1) / LANG_NOT_SUPPORTED (-2): keep the phone's default voice
                        Debug.LogWarning("[PathFinder] Voice language " + language + " not available; using the phone default.");
                }
                tts.Call<int>("setSpeechRate", speechRate);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PathFinder] Could not set the voice language: " + e.Message);
            }
            ready = true;
            if (!string.IsNullOrEmpty(pending))
            {
                string p = pending;
                pending = null;
                SpeakNow(p, true);
            }
        }

        void SpeakNow(string text, bool interrupt)
        {
            int mode = interrupt ? 0 : 1;   // QUEUE_FLUSH : QUEUE_ADD
            try
            {
                using (var bundle = new AndroidJavaObject("android.os.Bundle"))
                    tts.Call<int>("speak", text, mode, bundle, "pf" + (++utterance));   // API 21+
            }
            catch (System.Exception)
            {
                try
                {
                    using (var map = new AndroidJavaObject("java.util.HashMap"))
                        tts.Call<int>("speak", text, mode, map);                        // older signature
                }
                catch (System.Exception e2)
                {
                    Debug.LogWarning("[PathFinder] Could not speak: " + e2.Message);
                }
            }
        }

        void OnDestroy()
        {
            if (tts == null) return;
            try { tts.Call<int>("stop"); tts.Call("shutdown"); } catch (System.Exception) { }
            tts.Dispose();
            tts = null;
        }
#endif

        /// <summary>Speak a sentence (if voice is on). interrupt = true cuts off whatever is being said.</summary>
        public void Say(string text, bool interrupt = true)
        {
            if (!voiceOn || string.IsNullOrEmpty(text)) return;
            if (text == lastSpoken && Time.unscaledTime - lastTime < repeatGuardSeconds) return;
            lastSpoken = text;
            lastTime = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (tts == null) return;
            if (!ready) { pending = text; return; }
            SpeakNow(text, interrupt);
#else
            Debug.Log("[PathFinder voice] " + text);
#endif
        }

        public void SetVoice(bool on)
        {
            voiceOn = on;
            try { PlayerPrefs.SetInt(PrefKey, on ? 1 : 0); PlayerPrefs.Save(); } catch (System.Exception) { }
            if (!on) Stop();
            lastSpoken = "";
        }

        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (tts != null) { try { tts.Call<int>("stop"); } catch (System.Exception) { } }
#endif
        }

        /// <summary>Turns "In 12 m: Turn left" style text into a natural spoken sentence.</summary>
        public static string Spoken(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string s = text.Replace(" m:", " metres,").Replace(" m ", " metres ").Replace(" – ", ", ").Replace(" - ", ", ");
            if (s.EndsWith(" m")) s = s.Substring(0, s.Length - 2) + " metres";
            return s;
        }
    }
}

