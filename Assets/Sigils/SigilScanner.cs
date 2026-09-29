// SpellSign - the one component that puts sigil recognition into a scene.
//
// Add this to an empty GameObject. PassthroughCameraAccess and LiveSigilRecognizer
// get added automatically (RequireComponent) -- no Inspector wiring needed.
//
// It does two jobs:
//   1. Camera startup: asks for the headset camera permission, only turns the
//      camera on once it's granted, and restarts it if it stops delivering frames. Ported from the spike's SigilCaptureRig, minus
//      the photo-capture tool (A button / pinch saving PNGs), which would clash
//      with gameplay input.
//   2. Relays recognitions as a static event, so gameplay can react without
//      needing a reference to this object:
//
//        void OnEnable()  => SigilScanner.Scanned += OnSigil;
//        void OnDisable() => SigilScanner.Scanned -= OnSigil;
//        void OnSigil(SigilResult r) { if (r.Type == SigilType.Circle) ... }
//
// Scanned fires once per newly confirmed shape (debounced), not every frame.
//
// Needs in AndroidManifest.xml (siblings of <application>):
//   <uses-permission android:name="android.permission.CAMERA" />
//   <uses-permission android:name="horizonos.permission.HEADSET_CAMERA" />
// Quest 3 / 3S on Horizon OS v74+ only. The XR Simulator has no camera API.

using System;
using System.Collections;
using Meta.XR;
using UnityEngine;
using UnityEngine.Android;

namespace SpellSign.Sigils
{
    [RequireComponent(typeof(PassthroughCameraAccess), typeof(LiveSigilRecognizer))]
    public class SigilScanner : MonoBehaviour
    {
        const string CameraPermission = "horizonos.permission.HEADSET_CAMERA";

        [Tooltip("How long to wait for the player to answer the camera permission prompt, in seconds.")]
        [SerializeField] float permissionTimeout = 90f;

        [Tooltip("If the camera delivers no frames for this long, switch it off and on again, in seconds.")]
        [SerializeField] float restartCameraAfter = 3f;

        public static event Action<SigilResult> Scanned;

        PassthroughCameraAccess cameraAccess;
        LiveSigilRecognizer recognizer;

        public LiveSigilRecognizer Recognizer => recognizer;

        void Awake()
        {
            cameraAccess = GetComponent<PassthroughCameraAccess>();
            recognizer = GetComponent<LiveSigilRecognizer>();

            // Don't let the camera start before the permission is granted.
            cameraAccess.enabled = false;
        }

        void OnEnable() => recognizer.OnSigilRecognized += Relay;
        void OnDisable() => recognizer.OnSigilRecognized -= Relay;

        void Relay(SigilResult result) => Scanned?.Invoke(result);

        IEnumerator Start()
        {
            // Log only: PassthroughCameraAccess disables itself if it can't play.
            if (!PassthroughCameraAccess.IsSupported)
                Debug.LogWarning("[SigilScanner] Passthrough Camera API reports unsupported on this device -- recognition will likely stay idle.");

            if (!Permission.HasUserAuthorizedPermission(CameraPermission))
            {
                Debug.Log("[SigilScanner] requesting camera permission...");
                Permission.RequestUserPermission(CameraPermission);

                // Poll instead of using the callbacks: the callback fires inconsistently
                // when the dialog is dismissed by taking the headset off.
                float elapsed = 0f;
                while (!Permission.HasUserAuthorizedPermission(CameraPermission)
                       && elapsed < permissionTimeout)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            if (!Permission.HasUserAuthorizedPermission(CameraPermission))
            {
                Debug.LogError("[SigilScanner] camera permission DENIED -- sigil recognition off.");
                yield break;
            }

            // One frame of slack between grant and enable, otherwise the first start fails silently.
            yield return null;
            cameraAccess.enabled = true;
            Debug.Log("[SigilScanner] camera enabled");

            // Watchdog. PassthroughCameraAccess disables itself when Play() fails and
            // never retries -- e.g. when Build And Run launches the app before the
            // headset is on. Recognition then sits idle for the whole session.
            float notPlayingFor = 0f;
            while (true)
            {
                yield return null;

                if (cameraAccess.IsPlaying)
                {
                    notPlayingFor = 0f;
                    continue;
                }

                notPlayingFor += Time.unscaledDeltaTime;
                if (notPlayingFor < restartCameraAfter) continue;

                notPlayingFor = 0f;
                Debug.LogWarning("[SigilScanner] camera delivered no frames for " + restartCameraAfter + "s, restarting it");
                cameraAccess.enabled = false;
                yield return null;
                cameraAccess.enabled = true;
            }
        }
    }
}
