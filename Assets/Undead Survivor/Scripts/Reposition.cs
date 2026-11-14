using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Runs after CinemachineBrain (execution order 100) so the camera is already at its final position for this frame
[DefaultExecutionOrder(101)]
public class Reposition : MonoBehaviour
{
    const float GroundMargin = 0.25f;

    static bool isCreatingGroundCopy;

    Collider2D col;
    Rigidbody2D rb;
    Vector3 groundStartPosition;
    Vector3 groundStartCenter;
    Vector3 groundSize;
    bool isGround;
    readonly List<Transform> groundCopies = new List<Transform>();

    void Awake()
    {
        if (isCreatingGroundCopy)
        {
            // Ground copies are visual only and are positioned by their original tilemap
            enabled = false;
            Destroy(this);
            return;
        }

        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
    }

    void Start()
    {
        if (!CompareTag("Ground"))
            return;

        Tilemap tilemap = GetComponent<Tilemap>();
        if (tilemap == null)
        {
            Debug.LogError("Ground requires a Tilemap component.", this);
            return;
        }

        tilemap.CompressBounds();
        Bounds bounds = tilemap.localBounds;
        groundSize = transform.TransformVector(bounds.size);
        groundSize.x = Mathf.Abs(groundSize.x);
        groundSize.y = Mathf.Abs(groundSize.y);
        if (groundSize.x <= 0f || groundSize.y <= 0f)
        {
            Debug.LogError("Ground Tilemap must contain a non-empty rectangular tile area.", this);
            return;
        }

        groundStartPosition = transform.position;
        groundStartCenter = transform.TransformPoint(bounds.center);
        isGround = true;
    }

    void LateUpdate()
    {
        if (isGround)
            RepositionGround();
        else if (CompareTag("Enemy"))
            RepositionEnemy();
    }

    void RepositionGround()
    {
        if (!CameraView.TryGetWorldRect(transform.position.z, GroundMargin, out Rect view))
            return;

        // The four ground tilemaps form a 2x2 block, so each one repeats every two tile lengths.
        // Fill every repeat that touches the camera view, cloning this tilemap when the view is wider than the block.
        float width = groundSize.x * 2f;
        float height = groundSize.y * 2f;
        float halfX = groundSize.x * 0.5f;
        float halfY = groundSize.y * 0.5f;
        int minX = Mathf.CeilToInt((view.xMin - groundStartCenter.x - halfX) / width);
        int maxX = Mathf.FloorToInt((view.xMax - groundStartCenter.x + halfX) / width);
        int minY = Mathf.CeilToInt((view.yMin - groundStartCenter.y - halfY) / height);
        int maxY = Mathf.FloorToInt((view.yMax - groundStartCenter.y + halfY) / height);

        int count = 0;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Transform ground = count == 0 ? transform : GetGroundCopy(count - 1);
                Vector3 position = groundStartPosition + new Vector3(x * width, y * height, 0f);
                if (ground.position != position)
                    ground.position = position;
                count++;
            }
        }

        for (int i = Mathf.Max(count - 1, 0); i < groundCopies.Count; i++)
        {
            if (groundCopies[i].gameObject.activeSelf)
                groundCopies[i].gameObject.SetActive(false);
        }
    }

    Transform GetGroundCopy(int index)
    {
        if (index < groundCopies.Count)
        {
            GameObject existing = groundCopies[index].gameObject;
            if (!existing.activeSelf)
                existing.SetActive(true);
            return existing.transform;
        }

        GameObject copy;
        isCreatingGroundCopy = true;
        try
        {
            copy = Instantiate(gameObject, transform.parent);
        }
        finally
        {
            isCreatingGroundCopy = false;
        }

        copy.name = $"{name} (Copy {index + 1})";
        foreach (Collider2D copyCollider in copy.GetComponents<Collider2D>())
            copyCollider.enabled = false;
        if (copy.TryGetComponent(out Rigidbody2D copyBody))
            copyBody.simulated = false;

        groundCopies.Add(copy.transform);
        return copy.transform;
    }

    void RepositionEnemy()
    {
        if (!col.enabled || !GameManager.instance.isLive)
            return;
        // 적 떼는 화면을 가로지른 뒤 스스로 사라지므로 재배치하지 않음
        if (TryGetComponent(out Enemy swarmCheck) && (swarmCheck.IsSwarm || swarmCheck.InSpawnGrace))
            return;

        float z = transform.position.z;
        if (!CameraView.TryGetWorldRect(z, CameraView.EnemyRepositionMargin, out Rect activeArea)
            || activeArea.Contains(transform.position))
            return;

        CameraView.TryGetWorldRect(z, CameraView.EnemySpawnMargin, out Rect spawnArea);
        Vector2 direction = GameManager.instance.player.inputVector;
        if (direction.sqrMagnitude < 0.01f)
            direction = spawnArea.center - (Vector2)transform.position;
        direction = Quaternion.Euler(0f, 0f, Random.Range(-30f, 30f)) * direction;

        Vector3 position = CameraView.PointOnEdge(spawnArea, direction);
        position.z = z;
        if (rb != null)
            rb.position = position;
        transform.position = position;
    }
}
