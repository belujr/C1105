using UnityEngine;
using System.Collections.Generic;

public enum TokenType
{
    Melee,
    Disruption,
    Heavy,
    AgileFlanker,
    Ranged
}

public class GlobalTokenManager : MonoBehaviour
{
    public static GlobalTokenManager Instance { get; private set; }

    [System.Serializable]
    public struct TokenCategory
    {
        public TokenType type;
        public int maxTokens;
        [Tooltip("Minimum delay in seconds before a released token of this type can be re-assigned to any enemy.")]
        public float tokenSwitchDelay;
        [Tooltip("Displays current active token holders in real-time.")]
        public int activeTokensCount;
    }

    [Header("Token Configuration")]
    [SerializeField] 
    private List<TokenCategory> tokenCategories = new List<TokenCategory>
    {
        new TokenCategory { type = TokenType.Melee, maxTokens = 2, tokenSwitchDelay = 1.0f, activeTokensCount = 0 },
        new TokenCategory { type = TokenType.Disruption, maxTokens = 1, tokenSwitchDelay = 1.5f, activeTokensCount = 0 },
        new TokenCategory { type = TokenType.Heavy, maxTokens = 1, tokenSwitchDelay = 2.0f, activeTokensCount = 0 }
    };

    // Internal tracking dictionaries ensuring zero-GC lookups
    private Dictionary<TokenType, HashSet<Transform>> tokenHolders = new Dictionary<TokenType, HashSet<Transform>>();
    private Dictionary<TokenType, int> maxTokenLimits = new Dictionary<TokenType, int>();
    private Dictionary<TokenType, float> tokenSwitchDelays = new Dictionary<TokenType, float>();
    private Dictionary<TokenType, float> nextAvailableTimes = new Dictionary<TokenType, float>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitializeTokens();
    }

    private void InitializeTokens()
    {
        foreach (var category in tokenCategories)
        {
            if (!maxTokenLimits.ContainsKey(category.type))
            {
                maxTokenLimits.Add(category.type, category.maxTokens);
                tokenHolders.Add(category.type, new HashSet<Transform>());
                tokenSwitchDelays.Add(category.type, category.tokenSwitchDelay);
                nextAvailableTimes.Add(category.type, 0f);
            }
        }
    }

    /// <summary>
    /// Attempts to acquire an attack token for a specific enemy transform.
    /// Returns true if granted, false if capacity is maxed out or switch cooldown is active.
    /// </summary>
    public bool RequestToken(Transform enemyTransform, TokenType type)
    {
        if (!maxTokenLimits.ContainsKey(type)) return false;

        HashSet<Transform> holders = tokenHolders[type];
        
        // If this enemy already holds the token, validate and return true
        if (holders.Contains(enemyTransform)) return true;

        // Check if the global category switch delay is still active
        if (Time.time < nextAvailableTimes[type]) return false;

        // Check if category capacity allows issuing a new token
        if (holders.Count < maxTokenLimits[type])
        {
            holders.Add(enemyTransform);
            SyncInspectorCount(type, holders.Count);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Releases an attack token held by an enemy and triggers the switch delay cooldown.
    /// </summary>
    public void ReleaseToken(Transform enemyTransform, TokenType type)
    {
        if (!maxTokenLimits.ContainsKey(type)) return;

        HashSet<Transform> holders = tokenHolders[type];
        if (holders.Contains(enemyTransform))
        {
            holders.Remove(enemyTransform);
            SyncInspectorCount(type, holders.Count);
            nextAvailableTimes[type] = Time.time + tokenSwitchDelays[type];
        }
    }

    /// <summary>
    /// Forces clean release of all tokens held by a specific enemy (mandatory on death or pool recycling).
    /// </summary>
    public void ReleaseAllTokensForEnemy(Transform enemyTransform)
    {
        foreach (var kvp in tokenHolders)
        {
            TokenType type = kvp.Key;
            HashSet<Transform> holders = kvp.Value;
            if (holders.Contains(enemyTransform))
            {
                holders.Remove(enemyTransform);
                SyncInspectorCount(type, holders.Count);
                nextAvailableTimes[type] = Time.time + tokenSwitchDelays[type];
            }
        }
    }

    private void SyncInspectorCount(TokenType type, int currentCount)
    {
        for (int i = 0; i < tokenCategories.Count; i++)
        {
            if (tokenCategories[i].type == type)
            {
                var cat = tokenCategories[i];
                cat.activeTokensCount = currentCount;
                tokenCategories[i] = cat;
                break;
            }
        }
    }
}