using UnityEngine;
using TMPro;

public class SoulManager : MonoBehaviour
{
    public static SoulManager Instance { get; private set; }

    [Header("Currency Settings")]
    [SerializeField] private int totalSouls = 0;
    public int TotalSouls => totalSouls;

    [Header("UI Reference")]
    [Tooltip("Assign the TextMeshPro text element placed in the bottom right of the screen.")]
    public TMP_Text soulCounterText;

    // Event fired whenever soul currency changes
    public static event System.Action<int> OnSoulsChanged;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        UpdateUI();
    }

    public void AddSouls(int amount)
    {
        if (amount <= 0) return;
        totalSouls += amount;
        UpdateUI();
        OnSoulsChanged?.Invoke(totalSouls);
    }

    public bool SpendSouls(int amount)
    {
        if (amount <= 0 || totalSouls < amount) return false;
        totalSouls -= amount;
        UpdateUI();
        OnSoulsChanged?.Invoke(totalSouls);
        return true;
    }

    private void UpdateUI()
    {
        if (soulCounterText != null)
        {
            soulCounterText.text = totalSouls.ToString("N0");
        }
    }
}