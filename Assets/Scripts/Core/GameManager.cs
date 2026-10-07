using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Central game manager. Handles player spawning, score, match state.
/// Place on a single GameObject in the scene alongside GravitySystem.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Match Settings")]
    public GameMode gameMode = GameMode.FreeForAll;
    public int maxPlayers = 20;
    public float matchDuration = 600f;  // seconds

    [Header("Teams (Team Deathmatch)")]
    [Tooltip("Teammates can damage each other when enabled.")]
    public bool friendlyFire = false;
    [Tooltip("First team to reach this many kills wins. 0 = play until the timer runs out.")]
    public int teamKillLimit = 30;
    [Tooltip("Planet Capture: first team to this many points wins. 0 = play until the timer runs out.")]
    public int captureScoreLimit = 100;

    [Header("Spawn")]
    public Transform[] spawnPoints;
    public GameObject playerPrefab;

    [Header("Events")]

    [Header("Enemies (PvE)")]
    public GameObject enemyPrefab;
    public Transform[] enemySpawnPoints;
    [Tooltip("Flying Guardian Drone template (built by EnemySpawnSetup).")]
    public GameObject droneEnemyPrefab;
    [Tooltip("Zombie template (built by EnemySpawnSetup). They rise from the ground when there is noise.")]
    public GameObject zombieEnemyPrefab;
    [Tooltip("Zombies already wandering each planet at the start (more rise from the ground when noise is made).")]
    public int zombiesPerPlanet = 3;
    [Tooltip("Elite Soldiers spawned on each planet. 0 = use 'Enemy Count' spread randomly instead.")]
    public int enemiesPerPlanet = 2;
    [Tooltip("Guardian Drones spawned on each planet.")]
    public int dronesPerPlanet = 1;
    [Tooltip("Seconds before a killed enemy is replaced by a new one on the same planet. 0 = no respawn.")]
    public float enemyRespawnDelay = 25f;
    public int enemyCount = 5;   // only used when enemiesPerPlanet is 0

    // TeamDeathmatch is appended last so existing serialized GameMode values keep their meaning.
    public enum GameMode { FreeForAll, PlanetCapture, Survival, TeamDeathmatch }

    private float _matchTimer;
    private bool _matchActive;
    private readonly Dictionary<int, PlayerStats> _players = new();  // by PlayerId
    private readonly Dictionary<Team, int> _teamScores = new();
    private readonly Dictionary<int, int> _playerKills = new();  // PvP leaderboard
    private readonly Dictionary<int, int> _pveScore = new();     // enemies killed, tracked separately

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // While the main menu is up the match waits; the menu's PLAY button calls StartMatch().
        if (MainMenu.BlocksStart) return;
        StartMatch();
    }

    /// <summary>
    /// Changes the game mode before the match starts (used by the main menu) and re-deals teams
    /// for the players that already registered under the previous mode.
    /// </summary>
    public void SetGameMode(GameMode mode)
    {
        gameMode = mode;

        var existing = new List<PlayerStats>(_players.Values);
        _players.Clear();
        foreach (var p in existing)
        {
            if (p == null) continue;
            p.SetTeam(Team.None);     // forget the team from the old mode
            RegisterPlayer(p);        // assigns a balanced team again if the new mode uses teams
        }
    }

    void Update()
    {
        if (!_matchActive) return;

        _matchTimer -= Time.deltaTime;
        if (_matchTimer <= 0f) EndMatch();

    }

    public void StartMatch()
    {
        _matchTimer = matchDuration;
        _matchActive = true;
        Debug.Log($"Match started: {gameMode}");

        if (gameMode == GameMode.PlanetCapture && GetComponent<PlanetCaptureMode>() == null)
            gameObject.AddComponent<PlanetCaptureMode>();

        SpawnEnemies();
        SetUpZombies();
    }

    public void EndMatch()
    {
        if (!_matchActive) return;
        _matchActive = false;
        Debug.Log("Match ended.");

        if (IsTeamMode)
        {
            Team winner = GetLeadingTeam();
            string msg = winner == Team.None ? "DRAW" : $"TEAM {TeamUtil.GetName(winner)} WINS";
            Debug.Log($"[Teams] {msg}  (Pink {GetTeamScore(Team.Pink)} - Cyan {GetTeamScore(Team.Cyan)})");
            HUD.Instance?.ShowEvent(msg, 6f);
        }

        // Rank players by PvP kills and hand off to the scoreboard UI.
        // Scoreboard's "Play Again" button restarts the current scene — there's no
        // separate lobby scene in the build yet, so that's the closest equivalent
        // to "trigger lobby return" for now.
        var ranking = new List<KeyValuePair<int, int>>(_playerKills);
        ranking.Sort((a, b) => b.Value.CompareTo(a.Value));
        Scoreboard.Instance?.Show(ranking, _pveScore);
    }

    /// <summary>
    /// Spawns AI enemies for this match: <see cref="enemiesPerPlanet"/> on EVERY planet
    /// (falls back to <see cref="enemyCount"/> spread randomly when that is 0). Each enemy
    /// remembers its planet so a replacement can respawn there after it dies.
    /// </summary>
    private void SpawnEnemies()
    {
        if (enemyPrefab == null || enemySpawnPoints == null || enemySpawnPoints.Length == 0) return;

        if (enemiesPerPlanet <= 0 && dronesPerPlanet <= 0)
        {
            for (int i = 0; i < enemyCount; i++)
                SpawnEnemyAt(enemySpawnPoints[Random.Range(0, enemySpawnPoints.Length)], null, false);
            return;
        }

        int soldiers = 0, drones = 0;
        foreach (var planet in FindObjectsByType<PlanetGravity>(FindObjectsSortMode.None))
        {
            var points = EnemyPointsOn(planet);
            if (points.Count == 0) continue;

            for (int i = 0; i < enemiesPerPlanet; i++)
            {
                SpawnEnemyAt(points[Random.Range(0, points.Count)], planet, false);
                soldiers++;
            }
            if (droneEnemyPrefab == null) continue;
            for (int i = 0; i < dronesPerPlanet; i++)
            {
                SpawnEnemyAt(points[Random.Range(0, points.Count)], planet, true);
                drones++;
            }
        }
        Debug.Log($"[Enemies] Spawned {soldiers} Elite Soldiers and {drones} Guardian Drones " +
                  $"({enemiesPerPlanet} + {dronesPerPlanet} per planet).");
    }

    private void SetUpZombies()
    {
        if (zombieEnemyPrefab == null || enemySpawnPoints == null || enemySpawnPoints.Length == 0) return;
        var spawner = GetComponent<ZombieSpawner>();
        if (spawner == null) spawner = gameObject.AddComponent<ZombieSpawner>();
        spawner.Init(zombieEnemyPrefab, enemySpawnPoints, zombiesPerPlanet);
        Debug.Log($"[Enemies] Zombie horde ready: {zombiesPerPlanet} per planet at the start, more rise when there is noise.");
    }

    /// <summary>Enemy spawn points that sit on the surface of the given planet.</summary>
    private List<Transform> EnemyPointsOn(PlanetGravity planet)
    {
        var result = new List<Transform>();
        foreach (var point in enemySpawnPoints)
        {
            if (point == null) continue;
            // Spawn points are named SpawnPoint_<planet>_<n>; with hills / dunes the distance to the centre isn't constant
            bool byName = point.name.StartsWith("SpawnPoint_" + planet.planetName + "_");
            float dist = Vector3.Distance(point.position, planet.transform.position);
            if (byName && (planet.groundCollider != null || Mathf.Abs(dist - planet.radius) < 3f)) result.Add(point);
        }
        return result;
    }

    private GameObject SpawnEnemyAt(Transform point, PlanetGravity planet, bool drone)
    {
        var prefab = drone && droneEnemyPrefab != null ? droneEnemyPrefab : enemyPrefab;

        // Drones start a little above the ground so their legs don't clip it before they hover up.
        Vector3 pos = point.position + (drone ? point.up * 2f : Vector3.zero);
        var enemy = Instantiate(prefab, pos, point.rotation);
        // The template is kept inactive (so it never runs on its own); clones must be switched on.
        enemy.SetActive(true);

        var home = enemy.GetComponent<EnemyHome>();
        if (home == null) home = enemy.AddComponent<EnemyHome>();
        home.planet = planet;
        home.isDrone = drone;
        return enemy;
    }

    /// <summary>Called by EnemyStats when an enemy dies — schedules a replacement of the same kind on the same planet.</summary>
    public void NotifyEnemyKilled(EnemyHome home)
    {
        if (home == null || home.planet == null || enemyRespawnDelay <= 0f) return;
        if (enemiesPerPlanet <= 0 && dronesPerPlanet <= 0) return;
        StartCoroutine(RespawnEnemyRoutine(home.planet, home.isDrone));
    }

    private IEnumerator RespawnEnemyRoutine(PlanetGravity planet, bool drone)
    {
        yield return new WaitForSeconds(enemyRespawnDelay);
        if (!_matchActive || planet == null || enemyPrefab == null) yield break;

        var points = EnemyPointsOn(planet);
        if (points.Count == 0) yield break;
        SpawnEnemyAt(points[Random.Range(0, points.Count)], planet, drone);
    }

    /// <summary>PvP kill — counts toward the competitive leaderboard.</summary>
    public void RegisterKill(int attackerId, int victimId)
    {
        if (!_playerKills.ContainsKey(attackerId)) _playerKills[attackerId] = 0;
        _playerKills[attackerId]++;
        Debug.Log($"Player {attackerId} killed Player {victimId}. Total: {_playerKills[attackerId]}");

        // Team score: the attacker's team gets the point (self-kills / unassigned players don't score).
        if (gameMode == GameMode.TeamDeathmatch && attackerId != victimId
            && _players.TryGetValue(attackerId, out var attacker) && attacker.team != Team.None)
        {
            _teamScores[attacker.team] = GetTeamScore(attacker.team) + 1;
            if (teamKillLimit > 0 && _teamScores[attacker.team] >= teamKillLimit) EndMatch();
        }
    }

    /// <summary>Called by each PlayerStats on Start. Assigns a balanced team in Team Deathmatch.</summary>
    public void RegisterPlayer(PlayerStats player)
    {
        if (player == null) return;
        _players[player.PlayerId] = player;

        if (IsTeamMode && player.team == Team.None)
        {
            int pink = 0, cyan = 0;
            foreach (var kv in _players)
            {
                if (kv.Value == player) continue;
                if (kv.Value.team == Team.Pink) pink++;
                else if (kv.Value.team == Team.Cyan) cyan++;
            }
            player.SetTeam(pink <= cyan ? Team.Pink : Team.Cyan);
            Debug.Log($"[Teams] Player {player.PlayerId} joined team {TeamUtil.GetName(player.team)}.");
        }
    }

    /// <summary>Modes where players are split into Pink/Cyan teams.</summary>
    public bool IsTeamMode => gameMode == GameMode.TeamDeathmatch || gameMode == GameMode.PlanetCapture;

    public IEnumerable<PlayerStats> Players => _players.Values;

    /// <summary>Adds points to a team (Planet Capture) and ends the match at the score limit.</summary>
    public void AddTeamScore(Team team, int points)
    {
        if (team == Team.None || points <= 0 || !_matchActive) return;
        _teamScores[team] = GetTeamScore(team) + points;
        if (gameMode == GameMode.PlanetCapture && captureScoreLimit > 0 && _teamScores[team] >= captureScoreLimit)
            EndMatch();
    }

    public int GetTeamScore(Team team) => _teamScores.TryGetValue(team, out var s) ? s : 0;

    /// <summary>Team with the most kills, or None on a tie.</summary>
    public Team GetLeadingTeam()
    {
        int pink = GetTeamScore(Team.Pink), cyan = GetTeamScore(Team.Cyan);
        if (pink == cyan) return Team.None;
        return pink > cyan ? Team.Pink : Team.Cyan;
    }

    /// <summary>PvE kill (an AI enemy died) — tracked separately from the PvP leaderboard.</summary>
    public void RegisterEnemyKill(int playerId)
    {
        if (!_pveScore.ContainsKey(playerId)) _pveScore[playerId] = 0;
        _pveScore[playerId]++;
        Debug.Log($"Player {playerId} defeated an enemy. PvE score: {_pveScore[playerId]}");
    }

    /// <summary>PvP kills for a player (0 if none yet) — shown on the HUD.</summary>
    public int GetKills(int playerId) => _playerKills.TryGetValue(playerId, out var kills) ? kills : 0;

    public int GetPveScore(int playerId) => _pveScore.TryGetValue(playerId, out var score) ? score : 0;

    public Transform GetSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return transform;
        return spawnPoints[Random.Range(0, spawnPoints.Length)];
    }

    public float MatchTimeRemaining => _matchTimer;
    public bool IsMatchActive => _matchActive;
}
}
