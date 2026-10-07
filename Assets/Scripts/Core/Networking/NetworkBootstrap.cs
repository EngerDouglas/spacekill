using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Optionally gets a minimal NetworkManager + UnityTransport running (as Host) for local testing.
///
/// DISABLED BY DEFAULT: in this project Netcode currently fails to initialise — its IL
/// post-processing isn't applied, so StartHost() throws "Allowed types is not equal to the
/// number of message type indices" and leaves a half-built NetworkManager that throws a
/// NullReferenceException every frame (thousands per minute, flooding Editor.log). Nothing
/// in the game needs the network right now (AI enemies run locally), so the host isn't started.
///
/// To retry networking after fixing the package (e.g. reimport Netcode / delete Library/ and
/// reopen), set <see cref="autoStartHost"/> to true.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class NetworkBootstrap : MonoBehaviour
{
    [Tooltip("Start a Netcode host automatically. Leave off until Netcode initialises cleanly.")]
    public bool autoStartHost = false;

    void Awake()
    {
        if (!autoStartHost) return;
        if (NetworkManager.Singleton != null) return; // scene already has one — leave it alone

        var netManager = gameObject.AddComponent<NetworkManager>();
        var transport = gameObject.AddComponent<UnityTransport>();
        netManager.NetworkConfig = new NetworkConfig { NetworkTransport = transport };

        bool started = false;
        try { started = netManager.StartHost(); }
        catch (System.Exception e) { Debug.LogError($"[NetworkBootstrap] StartHost threw: {e.Message}"); }

        if (!started)
        {
            // Don't leave a broken NetworkManager behind — it would throw every frame.
            Destroy(transport);
            Destroy(netManager);
            Debug.LogWarning("[NetworkBootstrap] Networking unavailable; continuing offline.");
            return;
        }
        Debug.Log("[NetworkBootstrap] Started as host.");
    }
}
}
