using System.Collections;
using UnityEngine;

// Runs a level from start to finish:
//   Not Started (start menu) -> Play -> Playing
//   -> Won (the boss dies) or Lost (the player dies) -> end menu
//   -> Restart puts everything back to a fresh Playing state.
//
// Before Play the dragon isn't there at all; pressing Play drops it in front
// of wherever the player is standing and looking at that moment.
//
// While the end menu is up the fight is frozen: spell recognition is off, the
// boss has stopped attacking and every projectile is gone.
public class GameFlow : MonoBehaviour
{
    public enum State { Playing, Won, Lost, NotStarted }
    public enum DebugStart { None, Won, Lost }

    [Header("What the level is made of")]
    public PlayerHealth player;
    public EnemyTarget boss;
    public DragonBoss dragon;
    [Tooltip("Both hands' recognizers. Turned off while the menu is up so touching a button can't cast a spell.")]
    public HandGestureRecognizer[] hands;
    public EndMenu menu;

    [Header("Start")]
    [Tooltip("Open with a start menu and begin the fight when Play is pressed. Off = the fight begins as soon as the game loads.")]
    public bool showStartMenu = true;

    [Header("Timing")]
    [Tooltip("Seconds between the fight ending and the menu appearing. Long enough for the boss's death blink to finish.")]
    public float menuDelay = 1.5f;

    [Header("Testing")]
    [Tooltip("Show the menu right at the start, to try its buttons without playing the fight first.")]
    public DebugStart debugStart = DebugStart.None;

    public State Current { get; private set; } = State.Playing;

    StartMenu startMenu;

    void OnEnable()
    {
        boss.Died += OnBossDied;
        player.Died += OnPlayerDied;
        menu.RestartPressed += Restart;
    }

    void OnDisable()
    {
        boss.Died -= OnBossDied;
        player.Died -= OnPlayerDied;
        menu.RestartPressed -= Restart;
    }

    void Start()
    {
        if (debugStart == DebugStart.Won) Finish(State.Won);
        else if (debugStart == DebugStart.Lost) Finish(State.Lost);
        else if (showStartMenu) WaitForPlay();
    }

    void WaitForPlay()
    {
        Current = State.NotStarted;
        SetHandsActive(false); // touching Play can't cast a spell
        boss.gameObject.SetActive(false);

        startMenu = gameObject.AddComponent<StartMenu>();
        startMenu.hands = hands;
        startMenu.PlayPressed += Play;
        startMenu.Show();
    }

    void Play()
    {
        startMenu.Hide();
        Restart(); // brings the dragon in, in front of the player, at full health
    }

    void OnBossDied() => Finish(State.Won);
    void OnPlayerDied() => Finish(State.Lost);

    void Finish(State result)
    {
        // Only the first ending counts: a projectile still in flight can kill
        // the player a moment after the boss died, or the other way around.
        if (Current != State.Playing) return;

        Current = result;
        FreezeFight();
        StartCoroutine(ShowMenuAfterDelay(result == State.Won));
    }

    IEnumerator ShowMenuAfterDelay(bool won)
    {
        yield return new WaitForSeconds(menuDelay);
        menu.Show(won);
    }

    void Restart()
    {
        menu.Hide();
        ClearProjectiles();

        player.Restore();
        boss.Revive();           // works on a living boss too: back to full health and starting element
        dragon.ResetForRestart(); // drops in front of the player again, attacks after the usual delay

        SetHandsActive(true);
        Current = State.Playing;
    }

    void FreezeFight()
    {
        SetHandsActive(false);
        dragon.StopAttacking();
        ClearProjectiles();
    }

    void SetHandsActive(bool active)
    {
        foreach (HandGestureRecognizer hand in hands)
            if (hand != null) hand.enabled = active;
    }

    static void ClearProjectiles()
    {
        foreach (BossProjectile shot in FindObjectsByType<BossProjectile>())
            Destroy(shot.gameObject);
        foreach (FireballProjectile spell in FindObjectsByType<FireballProjectile>())
            Destroy(spell.gameObject);
    }
}
