using UnityEngine;
using System.Collections.Generic;

public enum TokenType
{
    Melee,
    Disruption,
    Heavy,
    AgileFlanker,
    Ranged,
    Engage,
    OuterRing
}

public class GlobalTokenManager : MonoBehaviour
{
    public static GlobalTokenManager Instance { get; private set; }

    [System.Serializable]
    public struct TokenCategory
    {
        public TokenType type;
        public int maxTokens;
        [Tooltip("Minimum delay in seconds before a released token can be re-assigned.")]
        public float tokenSwitchDelay;
        public int activeTokensCount;
    }

    [Header("Token Configuration")]
    [SerializeField] 
    private List<TokenCategory> tokenCategories = new List<TokenCategory>
    {
        new TokenCategory { type = TokenType.Melee, maxTokens = 2, tokenSwitchDelay = 1.0f },
        new TokenCategory { type = TokenType.Disruption, maxTokens = 1, tokenSwitchDelay = 1.5f },
        new TokenCategory { type = TokenType.Heavy, maxTokens = 1, tokenSwitchDelay = 2.0f },
        new TokenCategory { type = TokenType.Engage, maxTokens = 2, tokenSwitchDelay = 0.8f }, // Capped strictly at 2 attackers!
        new TokenCategory { type = TokenType.OuterRing, maxTokens = 10, tokenSwitchDelay = 0.0f } 
    };

    private class TokenPool
    {
        public HashSet<Transform> ActiveHolders = new HashSet<Transform>();
        public int MaxCapacity;
        public float CooldownDelay;
        public float NextAvailableTime;
        public int InspectorIndex;
    }

    private Dictionary<TokenType, TokenPool> pools = new Dictionary<TokenType, TokenPool>();
    
    // Tracks when an enemy last held an Engage token to prevent them from immediately re-grabbing it
    private Dictionary<Transform, float> lastEngageReleaseTimes = new Dictionary<Transform, float>();
    private const float RE_ENGAGE_COOLDOWN = 2.5f; 

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializePools();
    }

    private void InitializePools()
    {
        for (int i = 0; i < tokenCategories.Count; i++)
        {
            TokenCategory category = tokenCategories[i];
            if (!pools.ContainsKey(category.type))
            {
                pools.Add(category.type, new TokenPool
                {
                    MaxCapacity = category.maxTokens,
                    CooldownDelay = category.tokenSwitchDelay,
                    InspectorIndex = i
                });
            }
        }
    }

    public bool RequestToken(Transform enemyTransform, TokenType type)
    {
        if (!pools.TryGetValue(type, out TokenPool pool)) return false;

        if (pool.ActiveHolders.Contains(enemyTransform)) return true;

        // If requesting an Engage token, check if this specific enemy is on post-attack cooldown
        if (type == TokenType.Engage && lastEngageReleaseTimes.TryGetValue(enemyTransform, out float lastRelease))
        {
            if (Time.time < lastRelease + RE_ENGAGE_COOLDOWN) return false; // Force rotation to a fresh enemy!
        }

        if (Time.time < pool.NextAvailableTime || pool.ActiveHolders.Count >= pool.MaxCapacity) return false;

        pool.ActiveHolders.Add(enemyTransform);
        SyncInspectorCount(pool);
        return true;
    }

    public void ReleaseToken(Transform enemyTransform, TokenType type)
    {
        if (pools.TryGetValue(type, out TokenPool pool))
        {
            if (pool.ActiveHolders.Remove(enemyTransform))
            {
                SyncInspectorCount(pool);
                pool.NextAvailableTime = Time.time + pool.CooldownDelay;

                if (type == TokenType.Engage)
                {
                    if (lastEngageReleaseTimes.ContainsKey(enemyTransform))
                        lastEngageReleaseTimes[enemyTransform] = Time.time;
                    else
                        lastEngageReleaseTimes.Add(enemyTransform, Time.time);
                }
            }
        }
    }

    public void ReleaseAllTokensForEnemy(Transform enemyTransform)
    {
        foreach (var pool in pools.Values)
        {
            if (pool.ActiveHolders.Remove(enemyTransform))
            {
                SyncInspectorCount(pool);
                pool.NextAvailableTime = Time.time + pool.CooldownDelay;
            }
        }
        lastEngageReleaseTimes.Remove(enemyTransform);
    }

    private void SyncInspectorCount(TokenPool pool)
    {
        var category = tokenCategories[pool.InspectorIndex];
        category.activeTokensCount = pool.ActiveHolders.Count;
        tokenCategories[pool.InspectorIndex] = category;
    }
}