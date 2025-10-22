using UnityEngine;
using UnityEngine.Animations.Rigging;

public class GameStateController : MonoBehaviour
{
    public TCP tcp;                     // drag your TCP in Inspector
    public GameObject VrRig;

    public BoneRenderer boneRenderer;

    public RigBuilder rigBuilder;
    public AppleSpawner appleSpawner;     // optional: assign to pause/resume spawns

    public int loopsToWin = 3;
    private int loopCount = 0;

    private enum GameState { Collecting, RefillRequested, EndGame }
    private GameState state = GameState.Collecting;

    void OnEnable()
    {
        if (tcp != null) tcp.OnRefillSignal += OnRefillSignal;
    }

    void OnDisable()
    {
        if (tcp != null) tcp.OnRefillSignal -= OnRefillSignal;
    }

    void Start()
    {
        EnterCollecting();
    }

    void OnRefillSignal(bool needRefill)
    {
        if (needRefill)
        {
            if (state == GameState.Collecting)
                EnterRefillRequested();
        }
        else
        {
            if (state == GameState.RefillRequested)
                ExitRefillAndResume();
        }
    }

    void EnterCollecting()
    {
        state = GameState.Collecting;
        if (boneRenderer) boneRenderer.enabled = false;
        if (rigBuilder) rigBuilder.enabled = false;
        if (VrRig) VrRig.SetActive(false);      

        if (appleSpawner) appleSpawner.enabled = true;
        Debug.Log("[Game] -> Collecting: gems active, body tracking OFF");
    }

    void EnterRefillRequested()
    {
        state = GameState.RefillRequested;
        if (appleSpawner) appleSpawner.enabled = false; // stop generating while refilling
        if (boneRenderer) boneRenderer.enabled = true;
        if (rigBuilder) rigBuilder.enabled = true;
        if (VrRig) VrRig.SetActive(true);      
        Debug.Log("[Game] -> RefillRequested: gems paused, body tracking ON");
    }

    void ExitRefillAndResume()
    {
        loopCount++;
        Debug.Log($"[Game] REFILL_DONE -> loopsCompleted={loopCount}");

        if (loopCount >= loopsToWin)
        {
            state = GameState.EndGame;
            if (appleSpawner) appleSpawner.enabled = false;
            if (boneRenderer) boneRenderer.enabled = false;
            if (rigBuilder) rigBuilder.enabled = false;
            if (VrRig) VrRig.SetActive(false);      
            Debug.Log("[Game] -> EndGame");
            return;
        }

        // Otherwise resume another collecting round
        EnterCollecting();
    }
}
