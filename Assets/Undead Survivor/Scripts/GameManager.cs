using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;
    [Header("#Game Control")]
    public bool isLive;
    public float gameTime;
    public float maxPlayTime = 60f;
    [Tooltip("보스전 중에는 제한 시간이 끝나도 자동 승리하지 않음 (BossDirector 가 설정)")]
    public bool bossFightActive;
    [Header("#Player Info")]
    public float health;
    public float maxHealth = 100;
    public int level;
    public int kill;
    public int exp;
    public int[] nextExp = {10, 30, 60, 100, 150, 210, 280, 360, 450, 550};
    [Header("#Managers")]
    public PoolManager poolManager;
    public Player player;
    public LevelUp uiLevelUp;
    public Result uiResult;
    public GameObject enemyCleaner;
    int damageBatchDepth;
    int pendingLevelUps;
    void Awake()
    {
        instance = this;
    }

    public void GameStart()
    {
        health = maxHealth;
        uiLevelUp.Select(0);
        Resume();
    }

    public void GameOver()
    {
        StartCoroutine(GameOverCoroutine());
    }

    IEnumerator GameOverCoroutine()
    {
        isLive = false;
        yield return new WaitForSeconds(0.5f);

        uiResult.gameObject.SetActive(true);
        uiResult.Lose();
        Stop();
    }

    public void GameWin()
    {
        StartCoroutine(GameWinCoroutine());
    }

    IEnumerator GameWinCoroutine()
    {
        isLive = false;
        enemyCleaner.SetActive(true);
        yield return new WaitForSeconds(0.5f);

        uiResult.gameObject.SetActive(true);
        uiResult.Win();
        Stop();
    }

    public void GameRestart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void Update()
    {
        if (!isLive)
            return;
        gameTime += Time.deltaTime;
        if (gameTime >= maxPlayTime)
        {
            gameTime = maxPlayTime;
            if (!bossFightActive)
                GameWin();
        }
    }
    public void GetExp()
    {
        if (!isLive)
            return;
        exp++;
        if (exp >= nextExp[Mathf.Min(level, nextExp.Length - 1)])
        {
            exp -= nextExp[Mathf.Min(level, nextExp.Length - 1)];
            level++;
            if (damageBatchDepth > 0)
                pendingLevelUps++;
            else
                uiLevelUp.Show();
        }
    }

    public void BeginDamageBatch()
    {
        damageBatchDepth++;
    }

    public void EndDamageBatch()
    {
        damageBatchDepth--;
        if (damageBatchDepth == 0 && isLive)
            ShowPendingLevelUp();
    }

    public bool ShowPendingLevelUp()
    {
        if (pendingLevelUps == 0)
            return false;
        pendingLevelUps--;
        uiLevelUp.Show();
        return true;
    }

    public void Stop()
    {
        isLive = false;
        Time.timeScale = 0f;
    }

    public void Resume()
    {
        isLive = true;
        Time.timeScale = 1f;
    }
}
