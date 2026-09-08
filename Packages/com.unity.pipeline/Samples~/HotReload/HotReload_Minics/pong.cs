using UnityEngine;
using Unity.Pipeline.HotReload;

// Pong sample for com.unity.pipeline in-place hot reload:
//  - Self-playing AI instead of the Input System (project has no com.unity.inputsystem).
//  - Material created in code (project has no Resources/DefaultMaterial; built-in RP).
//  - All members public: in-place [HotReload] method bodies may only touch public members.
//  - [HotReload] on each editable method (Start/Update/PlacePaddle/OnDisable), so editing a helper
//    like PlacePaddle takes effect too. `reload_file Assets/pong.cs`.
public class PongScript : MonoBehaviour
{
    public GameObject leftPaddle, rightPaddle, ball;
    public GameObject leftScoreGO, rightScoreGO, arenaGO;
    public LineRenderer arena;
    public TextMesh leftScoreText, rightScoreText;
    public Material mat;
    public Renderer ballRenderer;

    public float leftPaddleAngle  = 180f;
    public float rightPaddleAngle = 0f;
    public Vector3 ballVelocity;
    public bool ballIsLeftPaddleColor = false;
    public int leftLives  = 3;
    public int rightLives = 3;
    public bool gameOver  = false;

    public float ballSpeed          = 5f;
    public float circleRadius       = 2f;
    public float paddleAngularSpeed = 180f;
    public float gravity            = 3f;

    // Hot-reload verification markers (read back via eval to confirm the live body ran).
    public int updateTicks = 0;
    public string marker = "none";
    private Color leftColor ;
    private Color rightColor;

    [OnHotReload]
    public void OnReloaded()
    {
        Debug.Log($"Pong HotReloaded");
        OnDisable();
        Start();
    }

    [HotReload]
    public void Start()
    {
        leftColor = new Color(0.678f, 1f, 0.184f); // green-yellow (Color.greenYellow needs 6000.2+)
        rightColor = Color.magenta;
        
        mat = new Material(Shader.Find("Sprites/Default"));
        Camera cam = Camera.main;
        cam.orthographic = true;
        cam.orthographicSize = 12.5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.grey;
        cam.transform.position = new Vector3(0.0f, 0.0f, -10.0f);


        leftScoreGO = new GameObject("LeftScore");
        leftScoreGO.transform.position = new Vector3(-6f, 6f, 0f);
        leftScoreText = leftScoreGO.AddComponent<TextMesh>();
        leftScoreText.anchor = TextAnchor.MiddleCenter;
        leftScoreText.alignment = TextAlignment.Center;
        leftScoreText.text          = "Cyan: " + leftLives.ToString();
        leftScoreText.characterSize = 0.5f;
        leftScoreText.color         = leftColor;

        rightScoreGO = new GameObject("RightScore");
        rightScoreGO.transform.position = new Vector3(6f, 6f, 0f);
        rightScoreText = rightScoreGO.AddComponent<TextMesh>();
        rightScoreText.anchor = TextAnchor.MiddleCenter;
        rightScoreText.alignment = TextAlignment.Center;
        rightScoreText.text          = "Magenta: " + rightLives.ToString();
        rightScoreText.characterSize = 0.5f;
        rightScoreText.color         = rightColor;

        arenaGO = new GameObject("CircleIndicator");
        arenaGO.transform.position = new Vector3(0f, 0f, 0.5f);
        arena = arenaGO.AddComponent<LineRenderer>();
        arena.useWorldSpace = false;
        arena.loop          = true;
        arena.startWidth    = 0.1f;
        arena.endWidth      = 0.1f;
        arena.material      = mat;
        arena.material.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        arena.positionCount  = 64;
        int i = 0;
        while (i < 64)
        {
            float a = i * Mathf.PI * 2f / 64f;
            arena.SetPosition(i, new Vector3(Mathf.Cos(a) * circleRadius, Mathf.Sin(a) * circleRadius, 0f));
            i = i + 1;
        }

        leftPaddle = GameObject.CreatePrimitive(PrimitiveType.Quad);
        leftPaddle.transform.localScale = new Vector3(0.3f, 1.5f, 1f);
        Renderer lr = leftPaddle.GetComponent<Renderer>();
        lr.material       = mat;
        lr.material.color = leftColor;

        rightPaddle = GameObject.CreatePrimitive(PrimitiveType.Quad);
        rightPaddle.transform.localScale = new Vector3(0.3f, 1.5f, 1f);
        Renderer rr = rightPaddle.GetComponent<Renderer>();
        rr.material       = mat;
        rr.material.color = rightColor;

        PlacePaddle(leftPaddle,  leftPaddleAngle);
        PlacePaddle(rightPaddle, rightPaddleAngle);

        ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.transform.localScale   = new Vector3(0.4f, 0.4f, 0.4f);
        ballRenderer                = ball.GetComponent<Renderer>();
        ballRenderer.material       = mat;
        ballRenderer.material.color = rightColor;

        ballVelocity = new Vector3(ballSpeed, ballSpeed * 0.5f, 0f);
    }

    [HotReload]
    public void PlacePaddle(GameObject paddle, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        paddle.transform.localScale = new Vector3(.5f,2,1);
        paddle.transform.position = new Vector3(Mathf.Cos(rad) * circleRadius, Mathf.Sin(rad) * circleRadius, 0f);
        paddle.transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
    }

    [HotReload]
    public void Update()
    {
        updateTicks++;
        marker = "live";

        if (gameOver)
        {
            this.OnDisable();
            gameOver = false;
            this.Start();
            return;
        }
        
        
        float dt = Time.deltaTime;

        // Self-playing AI: each paddle tracks the ball while the ball wears its colour.
        float ballAng = Mathf.Atan2(ball.transform.position.y, ball.transform.position.x) * Mathf.Rad2Deg;
        if (ballIsLeftPaddleColor)
        {
            float diff = Mathf.DeltaAngle(leftPaddleAngle, ballAng);
            if (diff >  5f) leftPaddleAngle += paddleAngularSpeed * dt;
            if (diff < -5f) leftPaddleAngle -= paddleAngularSpeed * dt;
        }
        else
        {
            float diff = Mathf.DeltaAngle(rightPaddleAngle, ballAng);
            if (diff >  5f) rightPaddleAngle += paddleAngularSpeed * dt;
            if (diff < -5f) rightPaddleAngle -= paddleAngularSpeed * dt;
        }

        PlacePaddle(leftPaddle,  leftPaddleAngle);
        PlacePaddle(rightPaddle, rightPaddleAngle);

        ballVelocity = ballVelocity  + Vector3.down * gravity * dt * 1.2f;
        ball.transform.position = ball.transform.position + ballVelocity * dt  ;
        Vector3 bPos  = ball.transform.position;
        float   bDist = bPos.magnitude;
        float   bAng  = Mathf.Atan2(bPos.y, bPos.x) * Mathf.Rad2Deg;

        if (bDist > circleRadius - 0.3f && bDist < circleRadius + 0.2f)
        {
            Vector3 normal = bPos.normalized;
            if (Vector3.Dot(ballVelocity, normal) > 0f)
            {
                if (ballIsLeftPaddleColor)
                {
                    float diff = Mathf.DeltaAngle(bAng, leftPaddleAngle);
                    if (diff > -12f && diff < 12f)
                    {
                        ballVelocity = Vector3.Reflect(ballVelocity, -normal);
                        ball.transform.position = ball.transform.position - normal * 0.1f;
                        ballIsLeftPaddleColor = false;
                        ballRenderer.material.color = rightColor;
                    }
                }
                else
                {
                    float diff = Mathf.DeltaAngle(bAng, rightPaddleAngle);
                    if (diff > -12f && diff < 12f)
                    {
                        ballVelocity = Vector3.Reflect(ballVelocity, -normal);
                        ball.transform.position = ball.transform.position - normal * 0.1f;
                        ballIsLeftPaddleColor = true;
                        ballRenderer.material.color = leftColor;
                    }
                }
            }
        }

        if (bDist > 8f)
        {
            if (ballIsLeftPaddleColor)
            {
                leftLives -= 1;
                leftScoreText.text = "Cyan: " + leftLives.ToString();
            }
            else
            {
                rightLives -= 1;
                rightScoreText.text = "Magenta: " + rightLives.ToString();
            }

            if (leftLives <= 0 || rightLives <= 0)
            {
                gameOver = true;
            }
            else
            {
                ball.transform.position = new Vector3(0f, 0f, 0f);
                ballVelocity = new Vector3(ballSpeed, ballSpeed * 0.5f, 0f);
                ballIsLeftPaddleColor = rightLives < leftLives;
                if (ballIsLeftPaddleColor)
                {
                    ballRenderer.material.color = leftColor;
                }
                else
                {
                    ballRenderer.material.color = rightColor;
                }
            }
        }
    }

    [HotReload]
    public void OnDisable()
    {
        if (leftPaddle) GameObject.Destroy(leftPaddle);
        if (rightPaddle) GameObject.Destroy(rightPaddle);
        if (ball) GameObject.Destroy(ball);
        if (leftScoreGO) GameObject.Destroy(leftScoreGO);
        if (rightScoreGO) GameObject.Destroy(rightScoreGO);
        if (arenaGO) GameObject.Destroy(arenaGO);
        Camera.main.orthographicSize = 5.0f;
        Camera.main.clearFlags = CameraClearFlags.Skybox;
        Camera.main.orthographic = false;
    }
}
