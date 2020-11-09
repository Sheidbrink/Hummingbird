using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// A hummingbird Machine Learning Agent
/// </summary>
public class HummingbirdAgent : Agent
{
    [Tooltip("Force to apply when moving")]
    public float moveForce = 2f;

    [Tooltip("Speed to pitch up or down")]
    public float pitchSpeed = 100f;

    [Tooltip("Speed to rotate around the up axis")]
    public float yawSpeed = 100f;

    [Tooltip("Transform at tip of beak")]
    public Transform beakTip;

    [Tooltip("Agents camera")]
    public Camera agentCamera;

    [Tooltip("Training or gameplay")]
    public bool trainingMode;

    new private Rigidbody rigidbody;
    private FlowerArea flowerArea;
    private Flower nearestFlower;

    // Helps to make NN not be as jittery
    private float smoothPitchChange = 0f;
    private float smoothYawChange = 0f;

    private const float MaxPitchAngle = 80f;
    private const float BeakTipRadius = 0.008f;

    // Whether the agent is frozen (intentionally not flying)
    private bool frozen = false;

    /// <summary>
    /// The amount of nectar the agent has optained
    /// </summary>
    public float NectarObtained { get; private set; }

    public override void Initialize()
    {
        rigidbody = GetComponent<Rigidbody>();
        flowerArea = GetComponentInParent<FlowerArea>();

        if (!trainingMode) MaxStep = 0;
    }
    public override void OnEpisodeBegin()
    {
        if (trainingMode)
        {
            flowerArea.ResetFlowers();
        }

        NectarObtained = 0;

        rigidbody.velocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;

        bool inFrontOfFlower = true;
        if (trainingMode)
        {
            inFrontOfFlower = UnityEngine.Random.value > .5f;
        }

        MoveToSafeRandomPosition(inFrontOfFlower);

        UpdateNearestFlower();
    }

    private void UpdateNearestFlower()
    {
        foreach (Flower flower in flowerArea.Flowers)
        {
            if (nearestFlower == null && flower.HasNectar)
            {
                nearestFlower = flower;
            }
            else if (flower.HasNectar)
            {
                float distanceToFlower = Vector3.Distance(flower.transform.position, beakTip.position);
                float distanceToCurrentNearestFlower = Vector3.Distance(nearestFlower.transform.position, beakTip.position);

                if (!nearestFlower.HasNectar || distanceToCurrentNearestFlower > distanceToFlower)
                {
                    nearestFlower = flower;
                }
            }
        }
        
    }

    public void FreezeAgent()
    {
        Debug.Assert(trainingMode == false, "Freeze/Unfreeze not supported in training");
        frozen = true;
        rigidbody.Sleep();
    }

    public void UnFreezeAgent()
    {
        Debug.Assert(trainingMode == false, "Freeze/Unfreeze not supported in training");
        frozen = false;
        rigidbody.WakeUp();
    }

    /// <summary>
    /// Move agent to safe random position (doesn't collide)
    /// </summary>
    /// <param name="inFrontOfFlower">Whether to choose spot in front of flower</param>
    private void MoveToSafeRandomPosition(bool inFrontOfFlower)
    {
        bool safePositionFound = false;
        int attemptsRemaining = 100;

        Vector3 potentialPosition = Vector3.zero;
        Quaternion potentialRotation = new Quaternion();

        while(!safePositionFound && attemptsRemaining > 0)
        {
            attemptsRemaining--;
            if (inFrontOfFlower)
            {
                Flower randomFlower = flowerArea.Flowers[UnityEngine.Random.Range(0, flowerArea.Flowers.Count)];

                // Position 10, 20 cm in front of flower
                float distanceFromFlower = UnityEngine.Random.Range(.1f, .2f);
                potentialPosition = randomFlower.transform.position + randomFlower.FlowerUpVector * distanceFromFlower;

                // Point beak at flower (bird's head is center of transform)
                Vector3 toFlower = randomFlower.FlowerCenterPosition - potentialPosition;
                potentialRotation = Quaternion.LookRotation(toFlower, Vector3.up);
            }
            else
            {
                float height = UnityEngine.Random.Range(1.2f, 2.5f);
                float radius = UnityEngine.Random.Range(2f, 7f);
                Quaternion direction = Quaternion.Euler(0f, UnityEngine.Random.Range(-180f, 180f), 0f);
                potentialPosition = flowerArea.transform.position + Vector3.up * height + direction * Vector3.forward * radius;

                float pitch = UnityEngine.Random.Range(-60f, 60f);
                float yaw = UnityEngine.Random.Range(-180f, 180f);
                potentialRotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            Collider[] colliders = Physics.OverlapSphere(potentialPosition, 0.05f);

            safePositionFound = colliders.Length == 0;
        }
        Debug.Assert(safePositionFound, "Could not find safe position");
        transform.position = potentialPosition;
        transform.rotation = potentialRotation;
    }

    /// <summary>
    /// Collect vector observations from the environment
    /// </summary>
    /// <param name="sensor"></param>
    public override void CollectObservations(VectorSensor sensor)
    {
        if (nearestFlower == null)
        {
            sensor.AddObservation(new float[10]);
            return;
        }
        // Observe the agent's local rotation (4 observations)
        sensor.AddObservation(transform.localRotation.normalized);

        // Get a vector from the beak tip to the nearest flower (3 observations)
        Vector3 toFlower = nearestFlower.FlowerCenterPosition - beakTip.position;
        sensor.AddObservation(toFlower.normalized);

        // Whether the beak tip is in front of flower
        sensor.AddObservation(Vector3.Dot(toFlower.normalized, -nearestFlower.FlowerUpVector.normalized));

        // Whether the beak is pointing towards the flower
        sensor.AddObservation(Vector3.Dot(beakTip.forward.normalized, -nearestFlower.FlowerUpVector.normalized));

        // distance from tip to flower
        sensor.AddObservation(toFlower.magnitude / FlowerArea.AreaDiameter);

        // 10 total
    }

    /// <summary>
    /// When Behavior Type is set to "Heuristic Only" on the agent's Behavior Parameters,
    /// this function will be called. Its return values will be fed into
    /// <see cref="OnActionReceived(float[])"/> instead of using the neural network
    /// </summary>
    /// <param name="actionsOut">An output action array</param>
    public override void Heuristic(float[] actionsOut)
    {
        Vector3 forward = Vector3.zero;
        Vector3 left = Vector3.zero;
        Vector3 up = Vector3.zero;
        float pitch = 0f;
        float yaw = 0f;

        /*
        forward = transform.forward * Input.GetAxis("Vertical");
        left = transform.right * Input.GetAxis("Horizontal");
        */
        if (Input.GetKey(KeyCode.W)) forward = transform.forward;
        else if (Input.GetKey(KeyCode.S)) forward = -transform.forward;

        if (Input.GetKey(KeyCode.A)) left = -transform.right;
        else if (Input.GetKey(KeyCode.D)) left = transform.right;

        if (Input.GetKey(KeyCode.E)) up = transform.up;
        else if (Input.GetKey(KeyCode.C)) up = -transform.up;

        if (Input.GetKey(KeyCode.UpArrow)) pitch = -1f;
        else if (Input.GetKey(KeyCode.DownArrow)) pitch = 1f;

        if (Input.GetKey(KeyCode.LeftArrow)) yaw = -1f;
        else if (Input.GetKey(KeyCode.RightArrow)) yaw = 1f;

        Vector3 combined = (forward + left + up).normalized;
        actionsOut[0] = combined.x;
        actionsOut[1] = combined.y;
        actionsOut[2] = combined.z;
        actionsOut[3] = pitch;
        actionsOut[4] = yaw;
    }

    /// <summary>
    /// Called when an action is received from either the player input or the neural network
    /// 
    /// vectorAction[i] represents:
    /// 0: move vector x (+1 right, -1 left)
    /// 1: move vector y (+1 up, -1 down)
    /// 2: move vector z (+1 forward, -1 backward)
    /// 3: pitch angle (+1 pitch up, -1 pitch down)
    /// 4: yaw angle (+1 right, -1 left)
    /// </summary>
    /// <param name="vectorAction">Actions to take</param>
    public override void OnActionReceived(float[] vectorAction)
    {
        if (frozen) return;

        // Calc mov vector
        Vector3 move = new Vector3(vectorAction[0], vectorAction[1], vectorAction[2]);

        // Add force
        rigidbody.AddForce(move * moveForce);

        // Rotate
        Vector3 rotationVector = transform.rotation.eulerAngles;

        float pitchChange = vectorAction[3];
        float yawChange = vectorAction[4];

        // Calculate smooth rotation changes
        smoothPitchChange = Mathf.MoveTowards(smoothPitchChange, pitchChange, 2f * Time.fixedDeltaTime);
        smoothYawChange = Mathf.MoveTowards(smoothYawChange, yawChange, 2f * Time.fixedDeltaTime);

        // Calculate new Pitch Yaw
        float pitch = rotationVector.x + smoothPitchChange * Time.fixedDeltaTime * pitchSpeed;
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, -MaxPitchAngle, MaxPitchAngle);

        float yaw = rotationVector.y + smoothYawChange * Time.fixedDeltaTime * yawSpeed;

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void OnTriggerEnter(Collider other)
    {
        TriggerEnterOrStay(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TriggerEnterOrStay(other);
    }

    private void TriggerEnterOrStay(Collider collider)
    {
        // Is colliding with nectar?
        if (collider.CompareTag("nectar"))
        {
            Vector3 closestPointToBeakTip = collider.ClosestPoint(beakTip.position);
            // Make sure beak tip is closest position
            if (Vector3.Distance(beakTip.position, closestPointToBeakTip) < BeakTipRadius)
            {
                Flower flower = flowerArea.GetFlowerFromNectar(collider);
                float nectarReceived = flower.Feed(.01f);
                NectarObtained += nectarReceived;

                if (trainingMode)
                {
                    // Calculate reward
                    float bonus = .02f * Mathf.Clamp01(Vector3.Dot(transform.forward.normalized, -nearestFlower.FlowerUpVector.normalized));
                    AddReward(.01f + bonus);
                }

                if (!flower.HasNectar)
                {
                    UpdateNearestFlower();
                }
            }
        }
    }
    private void OnCollisionEnter(Collision collision)
    {
        if (trainingMode && collision.collider.CompareTag("boundary"))
        {
            AddReward(-.5f);
        }
    }

    private void Update()
    {
        // Draw line from beak tup to nearest flower
        if (nearestFlower != null)
            Debug.DrawLine(beakTip.position, nearestFlower.FlowerCenterPosition, Color.green);
    }
    private void FixedUpdate()
    {
        // Avoides when you try to steal the nectar from the agent before it gets there
        if (nearestFlower != null && !nearestFlower.HasNectar)
            UpdateNearestFlower();
    }
}