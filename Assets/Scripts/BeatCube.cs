using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EzySlice; // Required for the slicing effect

public class BeatCube : MonoBehaviour
{
    [Header("Cube Settings")]
    public SaberColor requiredColor; // Only the saber of this color can cut this block
    public float moveSpeed = 5f;
    public float missedZThreshold = -0.1f; // The Z coordinate where the cube gives up and disappears
    public bool ignoreDirectionRequirement = false; // Used for "Any" direction dot blocks
    
    [Header("Slicing Effect Tuning")]
    public Vector3 slicedPieceOffset = new Vector3(0.5f, 0.5f, 0f); // Where do the pieces instantly teleport to relative to the center?
    
    [Header("References")]
    public Material insideCutMaterial; // Optional: A material to fill the inside of the sliced halves
    public GameObject pointsPopupPrefab; // The floating +115 text 

    private bool hasBeenCut = false;

    void Start()
    {
        // If an inside material isn't provided, just use the cube's own material
        if (insideCutMaterial == null)
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr != null) insideCutMaterial = mr.material;
        }
    }

    void Update()
    {
        // The cube simply moves forward (towards the player) in world space
        // You can adjust this axis depending on how your level is rotated. 
        // Usually, Beat Saber cubes travel backward along the Z-axis.
        transform.position += new Vector3(0, 0, -moveSpeed) * Time.deltaTime;
        
        // Destroy the cube if it gets completely past the player (missed)
        if (transform.position.z < missedZThreshold)
        {
            if (!hasBeenCut)
            {
                // We missed the block! Break the combo string
                if (ScoreManager.Instance != null)
                {
                    ScoreManager.Instance.BreakCombo();
                }
            }
            Destroy(gameObject);
        }
    }

    // This is called by the BeatSaber script when its SphereCast hits this cube's collider
    public void GetHitBySaber(BeatSaber saber, Vector3 swingVelocity, Vector3 saberDirection, Vector3 hitPoint)
    {
        // Prevent doubling cutting the same block in one frame
        if (hasBeenCut) return;

        // 1. Is it the right color saber?
        if (saber.saberColor != requiredColor)
        {
            Debug.Log($"[BeatCube] Wrong Color! Needed {requiredColor}, but hit by {saber.saberColor}");
            
            // Hitting with the wrong color saber ALSO breaks your combo (Bad Cut)
            if (ScoreManager.Instance != null) ScoreManager.Instance.BreakCombo();
            return;
        }

        // 2. Did the player swing in the correct direction?
        // Let's make "transform.up" mean "cut down".
        // To cut down, your velocity needs to be pointing DOWN.
        // So we compare velocity to -transform.up
        float hitAngle = Vector3.Angle(swingVelocity, -transform.up);
        
        // Let's make it very forgiving. Anything under 90 degrees means 
        // they swung generally in the correct half-circle direction.
        // NOTE: Because your blocks move along -Z, the front faces might be physically backwards.
        // If your successful swings are consistently logging ~170, we need to check for angles > 90 instead.
        if (saber.ignoreDirectionRequirement == false && hitAngle < 90f)
        {
            Debug.Log($"[BeatCube] Wrong Swing Direction! Angle was {hitAngle:F1}. Velocity: {swingVelocity}");
            
            // Swinging from the wrong direction ALSO breaks your combo (Bad Cut)
            if (ScoreManager.Instance != null) ScoreManager.Instance.BreakCombo();
            return;
        }

        // --- SUCCESS! Slice the cube ---
        hasBeenCut = true;

        // ==========================================
        //  3. SCORING MATH 
        // ==========================================
        if (ScoreManager.Instance != null)
        {
            int totalCutScore = 0;

            // Score Part 1: PRE-SWING ANGLE (Up to 100 points)
            // Real Beat Saber uses exact angle tracking. We will roughly simulate it using the Velocity of the swing.
            // A faster swing equals a wider arc. Let's cap max score at 10.0 magnitude speed.
            float swingMaxSpeed = 8.0f; // Tweak this until 100 score feels fair but requires a healthy swing
            float velocityRatio = Mathf.Clamp01(swingVelocity.magnitude / swingMaxSpeed);
            int swingScore = Mathf.RoundToInt(velocityRatio * 100f);
            
            // Add a floor so tiny taps still get *some* points instead of 0
            swingScore = Mathf.Max(swingScore, 50); 
            totalCutScore += swingScore;

            // Score Part 2: ACCURACY (Up to 15 points)
            // Measure how close the exact hit point was to the very dead center of the cube.
            // We use a flat plane distance (ignore Z depth) for typical Beat Saber accuracy math.
            float distanceToCenter = Vector3.Distance(transform.position, hitPoint);
            
            // A standard cube is 1 meter wide. 0 distance = 15 points. >0.3 distance = 0 points.
            float accuracyRatio = 1f - Mathf.Clamp01(distanceToCenter / 0.3f);
            int accuracyScore = Mathf.RoundToInt(accuracyRatio * 15f);
            totalCutScore += accuracyScore;

            // Cap the final score at 115 just in case
            totalCutScore = Mathf.Clamp(totalCutScore, 0, 115);

            // Add to the global leaderboard
            ScoreManager.Instance.AddScore(totalCutScore);

            // Spawn the floating +115 text!
            if (pointsPopupPrefab != null)
            {
                GameObject popupParams = Instantiate(pointsPopupPrefab, hitPoint, Quaternion.identity);
                PointsPopup popupScript = popupParams.GetComponent<PointsPopup>();
                if (popupScript != null)
                {
                    popupScript.Setup(totalCutScore);
                }
            }
        }

        // Calculate the perfectly perpendicular plane to slice along based on how you SWUNG
        // This is what makes it slice diagonally if you swing diagonally!
        Vector3 cutNormal = Vector3.Cross(saberDirection, swingVelocity).normalized; 
        
        // Fallback just in case velocity was extremely small
        if (cutNormal == Vector3.zero) cutNormal = transform.right; 
        
        // Sliced halves!
        // IMPORTANT: We use transform.position (exact center of the cube) instead of hitPoint.
        // If we use hitPoint, the SphereCast often hits the very outside edge of the physical mesh,
        // and EzySlice fails to find triangles to cut, so it returns null and the cube just vanishes!
        
        Debug.Log($"[BeatCube] Attempting to Slice... Position: {transform.position}, Normal: {cutNormal}, Material: {(insideCutMaterial != null ? insideCutMaterial.name : "NULL")}");
        
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("[BeatCube] EzySlice failed! This object does not have a MeshFilter or a Mesh!");
        }

        GameObject[] parts = gameObject.SliceInstantiate(transform.position, cutNormal, insideCutMaterial);

        if (parts == null || parts.Length == 0)
        {
            Debug.LogError("[BeatCube] EzySlice returned NULL! The cut plane did not intersect the mesh, or the mesh isn't readable.");
        }
        else
        {
            for (int n = 0; n < parts.Length; n++)
            {
                // CRITICAL: Unparent them just in case EzySlice spawns them as children of THIS cube, 
                // which is about to be Destroyed in 2 lines!
                parts[n].transform.SetParent(null);
                
                // TELEPORT THEM: The user wants them to appear at a specific target location when cut
                float popDirection = (n == 0) ? -1f : 1f;
                // Move them outwards based on the cut angle, PLUS the custom offset the user typed in!
                Vector3 finalOffset = (cutNormal * popDirection * slicedPieceOffset.x) + 
                                      (Vector3.up * slicedPieceOffset.y) + 
                                      (transform.forward * slicedPieceOffset.z);
                                      
                parts[n].transform.position = transform.position + finalOffset;

                // Add colliders and physics to the severed halves
                MeshCollider mc = parts[n].AddComponent<MeshCollider>();
                mc.convex = true;

                Rigidbody rb = parts[n].AddComponent<Rigidbody>();
                
                // Keep the outward pop force, but maybe reduce it slightly now that they are teleporting
                rb.AddForce((cutNormal * popDirection + Vector3.up * 0.5f).normalized * 100f);
                
                // DRASTICALLY reduce their forward momentum so they hang in the air in front/beside you
                // instead of flying instantly behind your head at full speed
                rb.linearVelocity = new Vector3(0, 0, -moveSpeed * 0.1f);

                // Clean them up after 2 seconds
                Destroy(parts[n], 2.0f);
            }
        }

        // Destroy the original, untouched cube
        Destroy(gameObject);
    }
}
