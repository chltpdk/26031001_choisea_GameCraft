
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Numerics;
using Vortice.Direct2D1;
using System.Runtime.InteropServices;
using Vortice.Mathematics;

class GameMain : G2AppBase
{
    public override System.Drawing.Size ScreenSize
        => GameGlobal.ScreenSize;

    public override string GameName
        => GameGlobal.GameName;

    // 이미지 리소스
    private G2Texture? mainImage;
    private G2Texture? startImage;
    private G2Texture? quitImage;
    private G2Texture? stageImage;

    private float mainWidth;
    private float mainHeight;
    private float stageWidth;
    private float stageHeight;

    // 메뉴 기준 해상도
    private const float MenuWidth = 960f;
    private const float MenuHeight = 640f;

    // 맵 원본 기준 해상도
    private const float MapWidth = 1672f;
    private const float MapHeight = 941f;

    // 메뉴 버튼 위치
    private readonly RectangleF startButton =
        new RectangleF(310, 316, 340, 100);

    private readonly RectangleF quitButton =
        new RectangleF(310, 436, 340, 100);

    // 버튼 이미지 영역
    private readonly Rect startNormal =
        new Rect(181, 121, 1175, 360);

    private readonly Rect startPressed =
        new Rect(181, 546, 1176, 354);

    private readonly Rect quitNormal =
        new Rect(158, 123, 1219, 370);

    private readonly Rect quitPressed =
        new Rect(158, 562, 1219, 370);

    private bool mouseWasDown;
    private bool startCaptured;
    private bool quitCaptured;
    private bool startIsPressed;
    private bool quitIsPressed;
    private bool isPlaying;

    // 플레이어 방향
    private enum Direction
    {
        Down,
        Up,
        Left,
        Right
    }

    // 플레이어 이미지
    private readonly G2Texture?[,] playerImages =
        new G2Texture?[4, 2];

    private bool isShooting;
    private readonly G2Texture?[,] shootingImages = new G2Texture?[4, 2];

    private Direction facing = Direction.Down;

    private float playerX;
    private float playerY;

    private bool isWalking;
    private float walkTimer;

    // 플레이어 크기와 이동 속도
    private const float PlayerSize = 96f;
    private const float PlayerSpeed = 220f;

    // 발밑 충돌 판정
    private const float CollisionRadius = 15f;
    private const float FeetOffsetY = 26f;

    // 프레임 시간
    private readonly Stopwatch frameTimer =
        Stopwatch.StartNew();

    private double lastFrameTime;

    // 장애물 이미지 리소스
    private sealed class SpriteAsset
    {
        public G2Texture Texture;
        public float SourceWidth;
        public float SourceHeight;

        public SpriteAsset(string path)
        {
            using (Image bitmap = Image.FromFile(path))
            {
                SourceWidth = bitmap.Width;
                SourceHeight = bitmap.Height;
            }

            Texture = new G2Texture(path);
        }
    }

    // 장애물 정보
    private sealed class Obstacle
    {
        public SpriteAsset Sprite;

        public float X;
        public float Y;
        public float Width;
        public float Height;

        public RectangleF Collider;

        // 앞뒤 그리기 순서
        public float SortY => Collider.Bottom;

        public Obstacle(
            SpriteAsset sprite,
            float x,
            float y,
            float width,
            float height,
            RectangleF collider)
        {
            Sprite = sprite;

            X = x;
            Y = y;
            Width = width;
            Height = height;

            Collider = collider;
        }
    }

    // 장애물 이미지 저장
    private readonly Dictionary<string, SpriteAsset>
        obstacleSprites =
        new Dictionary<string, SpriteAsset>(
            StringComparer.OrdinalIgnoreCase);

    // 장애물 목록
    private readonly List<Obstacle> obstacles =
        new List<Obstacle>();

    // 건물 충돌 목록
    private readonly List<RectangleF> buildingColliders =
        new List<RectangleF>();

    private string playImageFolder = "";

    // Windows 입력 처리
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(
        out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(
        IntPtr hwnd,
        ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(
        int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static bool KeyDown(int key)
    {
        return (GetAsyncKeyState(key) & 0x8000) != 0;
    }

    private static bool IsGameFocused()
    {
        IntPtr hwnd =
            Process.GetCurrentProcess().MainWindowHandle;

        return hwnd != IntPtr.Zero &&
               GetForegroundWindow() == hwnd;
    }

    // 게임 초기화
    protected override void Initialize()
    {
        string resourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "resource");

        string uiFolder = Path.Combine(
            resourcePath,
            "img_ui");

        playImageFolder = Path.Combine(
            resourcePath,
            "img_play");

        string mainPath =
            Path.Combine(uiFolder, "main.png");

        string stagePath =
            Path.Combine(uiFolder, "sta.png");

        // 화면 이미지 로드
        mainImage = new G2Texture(mainPath);

        startImage = new G2Texture(
            Path.Combine(playImageFolder, "Start.png"));

        quitImage = new G2Texture(
            Path.Combine(playImageFolder, "Quit.png"));

        stageImage = new G2Texture(stagePath);

        using (Image bitmap = Image.FromFile(mainPath))
        {
            mainWidth = bitmap.Width;
            mainHeight = bitmap.Height;
        }

        using (Image bitmap = Image.FromFile(stagePath))
        {
            stageWidth = bitmap.Width;
            stageHeight = bitmap.Height;
        }

        // 캐릭터 방향별 걷기 이미지
        string[,] files =
        {
            { "down_L.png",  "down_R.png" },
            { "up_L.png",    "up_R.png" },
            { "left_L.png",  "left_R.png" },
            { "right_L.png", "right_R.png" }
        };

        for (int direction = 0; direction < 4; direction++)
        {
            for (int frame = 0; frame < 2; frame++)
            {
                string path = Path.Combine(
                    playImageFolder,
                    files[direction, frame]);

                playerImages[direction, frame] =
                    new G2Texture(path);
                shootingImages[direction, frame] = new G2Texture(
                    Path.Combine(playImageFolder, "shooting", files[direction, frame]));
            }
        }

        // 건물 및 장애물 생성
        CreateBuildings();
        CreateObstacles();
        InitializeZombies();

        // 플레이어 초기화
        ResetPlayer();

        InitializeIntroMusic(resourcePath);

        lastFrameTime =
            frameTimer.Elapsed.TotalSeconds;
    }

    // 맵 좌표 변환
    private RectangleF MapRect(
        float x,
        float y,
        float width,
        float height)
    {
        float sx = stageWidth / MapWidth;
        float sy = stageHeight / MapHeight;

        return new RectangleF(
            x * sx,
            y * sy,
            width * sx,
            height * sy);
    }

    // 건물 충돌 영역
    private void CreateBuildings()
    {
        buildingColliders.Clear();

        // 왼쪽 위
        buildingColliders.Add(
            MapRect(0, 0, 310, 166));

        // 오른쪽 위
        buildingColliders.Add(
            MapRect(1355, 0, 317, 166));

        // 왼쪽 아래
        buildingColliders.Add(
            MapRect(0, 682, 311, 259));

        // 오른쪽 아래
        buildingColliders.Add(
            MapRect(1357, 682, 315, 259));
    }

    // 장애물 이미지 가져오기
    private SpriteAsset GetObstacleSprite(
        string filename)
    {
        if (!obstacleSprites.TryGetValue(
            filename,
            out SpriteAsset? sprite))
        {
            string path = Path.Combine(
                playImageFolder,
                filename);

            sprite = new SpriteAsset(path);

            obstacleSprites.Add(filename, sprite);
        }

        return sprite;
    }

    // 장애물 추가
    private void AddObstacle(
        string filename,
        float x,
        float y,
        float width,
        float height,
        float hitX,
        float hitY,
        float hitW,
        float hitH)
    {
        RectangleF rect =
            MapRect(x, y, width, height);

        // 실제 충돌 영역
        RectangleF collider = new RectangleF(
            rect.X + rect.Width * hitX,
            rect.Y + rect.Height * hitY,
            rect.Width * hitW,
            rect.Height * hitH);

        obstacles.Add(new Obstacle(
            GetObstacleSprite(filename),
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            collider));
    }

    // 장애물 배치
    private void CreateObstacles()
    {
        obstacles.Clear();

        // 왼쪽 차
        AddObstacle(
            "green_car.png",
            110, 325,
            230, 190,
            0.10f, 0.50f,
            0.82f, 0.35f);

        // 오른쪽 차
        AddObstacle(
            "car.png",
            1345, 250,
            235, 190,
            0.10f, 0.48f,
            0.82f, 0.37f);

        // 초록 쓰레기 컨테이너 
        AddObstacle(
            "green_box.png",
            400, 730,
            150, 130,
            0.18f, 0.65f,
            0.67f, 0.26f);

        // 나무 박스
        AddObstacle(
            "Box.png",
            700, 640,
            92, 92,
            0.17f, 0.60f,
            0.66f, 0.30f);

        // 신호등
        AddObstacle(
            "signal.png",
            1050, 38,
            82, 162,
            0.32f, 0.82f,
            0.27f, 0.13f);

        // 쓰레기통
        AddObstacle(
            "trash.png",
            1275, 570,
            78, 88,
            0.24f, 0.65f,
            0.60f, 0.25f);

        // 꼬깔콘1
        AddObstacle(
            "Kkokka.png",
            570, 338,
            50, 70,
            0.31f, 0.74f,
            0.39f, 0.19f);

        // 꼬깔콘 2 
        AddObstacle(
            "Kkokka.png",
            1065, 505,
            50, 70,
            0.31f, 0.74f,
            0.39f, 0.19f);

        // 장애물을 Y축 기준으로 정렬
        obstacles.Sort(
            (a, b) => a.SortY.CompareTo(b.SortY));
    }

    // 원형 충돌 판정
    private static bool CircleIntersectsRect(
        float cx,
        float cy,
        float radius,
        RectangleF rect)
    {
        float closestX = Math.Clamp(
            cx,
            rect.Left,
            rect.Right);

        float closestY = Math.Clamp(
            cy,
            rect.Top,
            rect.Bottom);

        float dx = cx - closestX;
        float dy = cy - closestY;

        return dx * dx + dy * dy
            < radius * radius;
    }

    // 건물 + 장애물 충돌 검사
    private bool CollidesWithAnyBlock(
        float nextX,
        float nextY)
    {
        float feetX = nextX;
        float feetY = nextY + FeetOffsetY;

        // 건물 충돌
        foreach (RectangleF building in buildingColliders)
        {
            if (CircleIntersectsRect(
                feetX,
                feetY,
                CollisionRadius,
                building))
            {
                return true;
            }
        }

        // 장애물 충돌
        foreach (Obstacle obstacle in obstacles)
        {
            if (CircleIntersectsRect(
                feetX,
                feetY,
                CollisionRadius,
                obstacle.Collider))
            {
                return true;
            }
        }

        return false;
    }

    // 메인 메뉴 화면 배치
    private void GetMenuLayout(
        out Rect mainRect,
        out RectangleF startRect,
        out RectangleF quitRect)
    {
        float scale = Math.Min(
            ScreenSize.Width / mainWidth,
            ScreenSize.Height / mainHeight);

        float width = mainWidth * scale;
        float height = mainHeight * scale;

        float x =
            (ScreenSize.Width - width) / 2f;

        float y =
            (ScreenSize.Height - height) / 2f;

        mainRect =
            new Rect(x, y, width, height);

        float sx = width / MenuWidth;
        float sy = height / MenuHeight;

        startRect = new RectangleF(
            x + startButton.X * sx,
            y + startButton.Y * sy,
            startButton.Width * sx,
            startButton.Height * sy);

        quitRect = new RectangleF(
            x + quitButton.X * sx,
            y + quitButton.Y * sy,
            quitButton.Width * sx,
            quitButton.Height * sy);
    }

    // 게임 배경 화면 꽉 채우기
    private void GetStageLayout(
        out float scale,
        out float offsetX,
        out float offsetY)
    {
        float scaleX =
            ScreenSize.Width / stageWidth;

        float scaleY =
            ScreenSize.Height / stageHeight;

        scale = Math.Max(scaleX, scaleY);

        offsetX =
            (ScreenSize.Width - stageWidth * scale) / 2f;

        offsetY =
            (ScreenSize.Height - stageHeight * scale) / 2f;
    }

    // 마우스 위치 확인
    private bool TryGetMousePosition(
        out float mouseX,
        out float mouseY)
    {
        mouseX = 0;
        mouseY = 0;

        IntPtr hwnd =
            Process.GetCurrentProcess().MainWindowHandle;

        if (hwnd == IntPtr.Zero ||
            GetForegroundWindow() != hwnd)
        {
            return false;
        }

        if (!GetCursorPos(out NativePoint point))
            return false;

        if (!ScreenToClient(hwnd, ref point))
            return false;

        if (ScaleX <= 0 || ScaleY <= 0) return false;
        mouseX = point.X / ScaleX;
        mouseY = point.Y / ScaleY;

        return true;
    }

    // 매 프레임 업데이트
    protected override void Update()
    {
        ClearColor =
            new Color4(0f, 0f, 0f, 1f);

        double now =
            frameTimer.Elapsed.TotalSeconds;

        float dt = (float)Math.Clamp(
            now - lastFrameTime,
            0.0,
            0.05);

        lastFrameTime = now;

        UpdateIntroMusic(dt);
        UpdateGameCursor();

        if (isPlaying)
        {
            bool canUpdate = CanUpdateGameplay();
            UpdateGameCursor();
            if (!canUpdate)
            {
                isWalking = false;
                isShooting = false;
                return;
            }
            UpdatePlayer(dt);
            UpdateCombat(dt);
            UpdateZombies(dt);
            UpdateGameCursor();
        }
        else
        {
            UpdateMenu();
        }
    }

    // 메인 메뉴 입력
    private void UpdateMenu()
    {
        GetMenuLayout(
            out _,
            out RectangleF startRect,
            out RectangleF quitRect);

        bool validMouse =
            TryGetMousePosition(
                out float mx,
                out float my);

        bool mouseDown = KeyDown(0x01);

        bool onStart =
            validMouse && startRect.Contains(mx, my);

        bool onQuit =
            validMouse && quitRect.Contains(mx, my);

        // 마우스 클릭 시작
        if (mouseDown && !mouseWasDown)
        {
            startCaptured = onStart;
            quitCaptured = onQuit;
        }

        // 클릭 중 이미지 변경
        startIsPressed =
            mouseDown && startCaptured && onStart;

        quitIsPressed =
            mouseDown && quitCaptured && onQuit;

        // 마우스 클릭 종료
        if (!mouseDown && mouseWasDown)
        {
            if (startCaptured && onStart)
            {
                StartGame();
            }
            else if (quitCaptured && onQuit)
            {
                Close();
            }

            startCaptured = false;
            quitCaptured = false;
        }

        mouseWasDown = mouseDown;
    }

    // 플레이어 초기화
    private void ResetPlayer()
    {
        playerX = stageWidth * 0.485f;
        playerY = stageHeight * 0.47f;

        facing = Direction.Down;

        isShooting = false;
        isWalking = false;
        walkTimer = 0f;
    }

    // 게임 시작
    private void StartGame()
    {
        ResetPlayer();
        zombieWaves.Reset();
        playerSurvival.Reset();
        combat.Reset();
        enemyAttacks.Reset();
        ResetGameOverInput();
        isPaused = false;
        escapeWasDown = KeyDown(0x1B);
        isPlaying = true;
        UpdateGameCursor();
    }

    // 플레이어 이동
    private void UpdatePlayer(float dt)
    {
        if (!IsGameFocused())
        {
            isShooting = false;
            isWalking = false;
            walkTimer = 0f;
            return;
        }

        // 키보드 입력
        bool up =
            KeyDown(0x57) || KeyDown(0x26);

        bool down =
            KeyDown(0x53) || KeyDown(0x28);

        bool left =
            KeyDown(0x41) || KeyDown(0x25);

        bool right =
            KeyDown(0x44) || KeyDown(0x27);

        float dx = 0f;
        float dy = 0f;

        // 상하좌우 이동
        // 대각선 이동 불가능
        if (up && !down)
        {
            dy = -1f;
        }
        else if (down && !up)
        {
            dy = 1f;
        }
        else if (left && !right)
        {
            dx = -1f;
        }
        else if (right && !left)
        {
            dx = 1f;
        }

        // 이동하지 않는 경우
        if (dx == 0f && dy == 0f)
        {
            isWalking = false;
            walkTimer = 0f;
            return;
        }



        // 화면 경계 계산
        GetStageLayout(
            out float scale,
            out float offsetX,
            out float offsetY);

        float leftBound = Math.Max(
            0f, -offsetX / scale)
            + PlayerSize / 2f;

        float topBound = Math.Max(
            0f, -offsetY / scale)
            + PlayerSize / 2f;

        float rightBound = Math.Min(
            stageWidth,
            (ScreenSize.Width - offsetX) / scale)
            - PlayerSize / 2f;

        float bottomBound = Math.Min(
            stageHeight,
            (ScreenSize.Height - offsetY) / scale)
            - PlayerSize / 2f;

        // 이동량 계산
        float distance = PlayerSpeed * dt;

        // 빠른 이동으로 장애물을 통과하지 않도록
        // 이동을 작은 단계로 나누기
        int steps = Math.Max(
            1,
            (int)Math.Ceiling(distance / 4f));

        float stepX = dx * distance / steps;
        float stepY = dy * distance / steps;

        float oldX = playerX;
        float oldY = playerY;

        // 단계별 이동 및 충돌 검사
        for (int i = 0; i < steps; i++)
        {
            // X축 이동
            if (leftBound <= rightBound)
            {
                float nextX = Math.Clamp(
                    playerX + stepX,
                    leftBound,
                    rightBound);

                if (!CollidesWithAnyBlock(
                    nextX, playerY))
                {
                    playerX = nextX;
                }
            }

            // Y축 이동
            if (topBound <= bottomBound)
            {
                float nextY = Math.Clamp(
                    playerY + stepY,
                    topBound,
                    bottomBound);

                if (!CollidesWithAnyBlock(
                    playerX, nextY))
                {
                    playerY = nextY;
                }
            }
        }

        // 실제 이동 여부 확인
        isWalking =
            Math.Abs(playerX - oldX) > 0.001f ||
            Math.Abs(playerY - oldY) > 0.001f;

        // 걷기 애니메이션
        if (isWalking)
        {
            walkTimer += dt;
        }
        else
        {
            walkTimer = 0f;
        }
    }

    // 전체 화면 렌더링
    protected override void Render()
    {
        if (isPlaying)
        {
            RenderGame();
        }
        else
        {
            RenderMainMenu();
        }
    }

    // 메인 메뉴 그리기
    private void RenderMainMenu()
    {
        if (mainImage == null ||
            startImage == null ||
            quitImage == null)
        {
            return;
        }

        GetMenuLayout(
            out Rect mainRect,
            out RectangleF startRect,
            out RectangleF quitRect);

        // 메인 배경
        mainImage.Draw(
            mainRect,
            new Rect(
                0,
                0,
                mainWidth,
                mainHeight));

        // START 버튼
        startImage.Draw(
            new Rect(
                startRect.X,
                startRect.Y,
                startRect.Width,
                startRect.Height),
            startIsPressed
                ? startPressed
                : startNormal);

        // QUIT 버튼
        quitImage.Draw(
            new Rect(
                quitRect.X,
                quitRect.Y,
                quitRect.Width,
                quitRect.Height),
            quitIsPressed
                ? quitPressed
                : quitNormal);
    }

    // 장애물 그리기
    private void DrawObstacle(
        Obstacle obstacle,
        float scale,
        float offsetX,
        float offsetY)
    {
        obstacle.Sprite.Texture.Draw(
            new Rect(
                offsetX + obstacle.X * scale,
                offsetY + obstacle.Y * scale,
                obstacle.Width * scale,
                obstacle.Height * scale),
            new Rect(
                0,
                0,
                obstacle.Sprite.SourceWidth,
                obstacle.Sprite.SourceHeight));
    }

    // 캐릭터 그리기
    private void DrawPlayer(
        float scale,
        float offsetX,
        float offsetY)
    {
        // 걷기 프레임 계산
        int frame = isWalking
            ? (int)(walkTimer / 0.16f) % 2
            : 0;

   
        G2Texture? texture = isShooting
            ? shootingImages[(int)facing, frame]
            : playerImages[(int)facing, frame];

        if (texture == null)
            return;

        // 캐릭터 크기
        float size = PlayerSize * scale;

        // 캐릭터 위치
        float x =
            offsetX + playerX * scale - size / 2f;

        float y =
            offsetY + playerY * scale - size / 2f;

        texture.Draw(
            new Rect(
                x,
                y,
                size,
                size),
            new Rect(
                0,
                0,
                PlayerSize,
                PlayerSize),
            playerSurvival.InvulnerabilityRemaining > 0 &&
                (int)(playerSurvival.InvulnerabilityRemaining / 0.09f) % 2 == 0 ? 0.3f : 1f);

    }

    // 게임 화면 그리기
    private void RenderGame()
    {
        if (playerSurvival.IsGameOver)
        {
            RenderGameOver();
            return;
        }
        if (stageImage == null)
            return;

        GetStageLayout(
            out float scale,
            out float offsetX,
            out float offsetY);

        // 1. 배경 출력
        stageImage.Draw(
            new Rect(
                offsetX,
                offsetY,
                stageWidth * scale,
                stageHeight * scale),
            new Rect(
                0,
                0,
                stageWidth,
                stageHeight));


        RenderActors(scale, offsetX, offsetY);
    }

    // 리소스 해제
    public override void Dispose()
    {
        introMusic?.Dispose();
        introMusic = null;
        mainImage?.Dispose();
        startImage?.Dispose();
        quitImage?.Dispose();
        stageImage?.Dispose();
        DisposeCombat();
        DisposeZombies();
        foreach (G2Texture? texture in shootingImages)
        {
            texture?.Dispose();
        }

        // 캐릭터 이미지 해제
        foreach (G2Texture? texture in playerImages)
        {
            texture?.Dispose();
        }

        // 장애물 이미지 해제
        foreach (SpriteAsset asset in obstacleSprites.Values)
        {
            asset.Texture.Dispose();
        }

        obstacleSprites.Clear();
        obstacles.Clear();

        base.Dispose();
    }



    private readonly ZombieWaveSystem zombieWaves = new();
    private readonly PlayerSurvival playerSurvival = new();
    private readonly G2Texture?[,] zombieImages = new G2Texture?[3, 4];
    private readonly List<RectangleF> zombieBlockers = new();
    private readonly List<(float Depth, int Kind, int Index)> actorDrawOrder = new();
    private G2Font? waveFont;
    private bool isPaused;
    private bool escapeWasDown;
    private bool restartWasDown;

    private void InitializeZombies()
    {
        for (int kind = 0; kind < 3; kind++)
        for (int frame = 0; frame < 4; frame++)
            zombieImages[kind, frame] = new G2Texture(Path.Combine(playImageFolder, "zombies", "walking",
                $"melee_lv{kind + 1}_{frame}.png"));

        zombieBlockers.Clear();
        zombieBlockers.AddRange(buildingColliders);
        foreach (Obstacle obstacle in obstacles)
            zombieBlockers.Add(obstacle.Collider);
        waveFont = new G2Font("Malgun Gothic", 18f);
        InitializeGameOver();
        InitializeCombat();
        InitializeRangedEnemies();
    }

    private bool CanUpdateGameplay()
    {
        return ProcessGameplayInput(IsGameFocused(), KeyDown(0x1B), KeyDown(0x52) || KeyDown(0x0D));
    }

    private bool ProcessGameplayInput(bool focused, bool escapeDown, bool restartDown)
    {
        bool escapePressed = escapeDown && !escapeWasDown;
        bool restartPressed = restartDown && !restartWasDown;
        escapeWasDown = escapeDown;
        restartWasDown = restartDown;
        if (playerSurvival.IsGameOver)
        {
            if (focused && restartPressed) StartGame();
            else if (focused && escapePressed)
            {
                ReturnToMainMenu();
            }
            else
            {
                bool validMouse = TryGetMousePosition(out float x, out float y);
                ProcessGameOverMouse(focused && validMouse, KeyDown(0x01), new Vector2(x, y));
            }
            return false;
        }
        if (focused && escapePressed)
            isPaused = !isPaused;
        // 포커스가 없거나 일시정지 중이면 생존 시간과 생성 타이머도 멈춥니다.
        return focused && !isPaused;
    }

    private void UpdateZombies(float dt)
    {
        if (isPaused || playerSurvival.IsGameOver) return;
        GetStageLayout(out float scale, out float offsetX, out float offsetY);
        var viewport = new RectangleF(-offsetX / scale, -offsetY / scale,
            ScreenSize.Width / scale, ScreenSize.Height / scale);
        zombieWaves.Update(dt, viewport, new Vector2(playerX, playerY + FeetOffsetY), zombieBlockers);
        playerSurvival.Update(dt, new Vector2(playerX, playerY + FeetOffsetY), zombieWaves.Zombies);
        enemyAttacks.Update(dt, zombieWaves.Zombies, new Vector2(playerX, playerY + FeetOffsetY),
            viewport, zombieBlockers, playerSurvival);
        if (playerSurvival.IsGameOver)
        {
            isWalking = false;
            isShooting = false;
        }
    }

    private void DrawZombie(ZombieEnemy zombie, float scale, float offsetX, float offsetY)
    {
        // 96px 파일의 실제 발 위치(48,88)에 맞춰 모든 레벨의 발을 정렬합니다.
        float spriteScale = scale * ZombieEnemy.VisualScale;
        G2Texture? image = zombie.IsRanged ? rangedZombieImages[zombie.RangedLevel - 1, zombie.WalkFrame]
            : zombieImages[(int)zombie.Kind, zombie.WalkFrame];
        image?.Draw(
            new Rect(offsetX + zombie.Position.X * scale - 48f * spriteScale,
                offsetY + zombie.Position.Y * scale - 88f * spriteScale, 96f * spriteScale, 96f * spriteScale),
            new Rect(0, 0, 96, 96));
    }

    private void RenderActors(float scale, float offsetX, float offsetY)
    {
        actorDrawOrder.Clear();
        for (int index = 0; index < obstacles.Count; index++)
            actorDrawOrder.Add((obstacles[index].SortY, 0, index));
        for (int index = 0; index < zombieWaves.Zombies.Count; index++)
            actorDrawOrder.Add((zombieWaves.Zombies[index].Position.Y, 1, index));
        actorDrawOrder.Add((playerY + FeetOffsetY, 2, 0));
        actorDrawOrder.Sort(static (a, b) =>
        {
            int depth = a.Depth.CompareTo(b.Depth);
            if (depth != 0) return depth;
            int kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : a.Index.CompareTo(b.Index);
        });
        foreach (var actor in actorDrawOrder)
        {
            switch (actor.Kind)
            {
                case 0: DrawObstacle(obstacles[actor.Index], scale, offsetX, offsetY); break;
                case 1: DrawZombie(zombieWaves.Zombies[actor.Index], scale, offsetX, offsetY); break;
                default: DrawPlayer(scale, offsetX, offsetY); break;
            }
        }

        DrawCombat(scale, offsetX, offsetY);
        DrawEnemyAttacks(scale, offsetX, offsetY);
        int seconds = (int)zombieWaves.ElapsedSeconds;
        string status = $"시간 {seconds / 60:00}:{seconds % 60:00}  |  체력 {playerSurvival.Health}/{PlayerSurvival.MaximumHealth}  |  킬 {zombieWaves.KillCount}";
        waveFont?.DrawText(status, new Rect(17, 17, ScreenSize.Width - 28, 34), new Color4(0, 0, 0, 1));
        waveFont?.DrawText(status, new Rect(15, 15, ScreenSize.Width - 28, 34), new Color4(1, 1, 1, 1));
        if (isPaused)
        {
            waveFont?.DrawText("일시정지 · ESC로 계속", new Rect(ScreenSize.Width / 2f - 110,
                ScreenSize.Height / 2f, 280, 40), new Color4(1f, 0.9f, 0.6f, 1f));
        }
    }

    private void DisposeZombies()
    {
        foreach (G2Texture? image in zombieImages)
            image?.Dispose();
        waveFont?.Dispose();
        DisposeGameOver();
        DisposeRangedEnemies();
        zombieWaves.Reset();
    }





    private readonly CombatSystem combat = new();
    private ID2D1SolidColorBrush? bulletBrush;
    private ID2D1SolidColorBrush? impactBrush;

    private void InitializeCombat()
    {
        bulletBrush = RenderTarget.CreateSolidColorBrush(new Color4(1f, 0.87f, 0.4f, 1f));
        impactBrush = RenderTarget.CreateSolidColorBrush(new Color4(1f, 0.4f, 0.2f, 1f));
    }

    private void UpdateGameCursor()
    {
        // 창 내부에만 적용하며 메뉴/일시정지/게임 오버에서는 기본 커서로 복귀합니다.
        WindowCursor = isPlaying && !isPaused && !playerSurvival.IsGameOver
            ? System.Windows.Forms.Cursors.Cross : System.Windows.Forms.Cursors.Default;
    }

    private void UpdateCombat(float dt)
    {
        bool validAim = TryGetMousePosition(out float x, out float y) &&
            x >= 0 && x < ScreenSize.Width && y >= 0 && y < ScreenSize.Height;
        UpdateCombatAt(dt, validAim, KeyDown(0x01), new Vector2(x, y));
    }

    private void UpdateCombatAt(float dt, bool validAim, bool trigger, Vector2 screenAim)
    {
        if (!isPlaying || isPaused || playerSurvival.IsGameOver) return;
        GetStageLayout(out float scale, out float offsetX, out float offsetY);
        Vector2 aim = (screenAim - new Vector2(offsetX, offsetY)) / scale;
        Vector2 origin = new(playerX, playerY);
        Vector2 direction = aim - origin;
        validAim &= direction.LengthSquared() > 1f;
        if (validAim)
        {
            // 이동 키와 관계없이 마우스가 가리키는 쪽으로 사격 자세를 돌립니다.
            facing = MathF.Abs(direction.X) > MathF.Abs(direction.Y)
                ? direction.X > 0 ? Direction.Right : Direction.Left
                : direction.Y > 0 ? Direction.Down : Direction.Up;
        }
        Vector2 muzzleOffset = facing switch
        {
            Direction.Up => new(1, -39),
            Direction.Left => new(-25, -4),
            Direction.Right => new(28, -5),
            _ => new(0, 14)
        };
        isShooting = validAim && trigger;
        combat.Update(dt, isShooting, origin, origin + muzzleOffset, aim, zombieWaves, zombieBlockers);
    }

    private void DrawCombat(float scale, float offsetX, float offsetY)
    {
        Vector2 ToScreen(Vector2 p) => new(offsetX + p.X * scale, offsetY + p.Y * scale);
        if (bulletBrush != null)
            foreach (Bullet bullet in combat.Bullets)
            {
                Vector2 tip = ToScreen(bullet.Position);
                Vector2 tail = ToScreen(bullet.Position - bullet.Direction * 15f);
                RenderTarget.DrawLine(tail, tip, bulletBrush, Math.Max(2, 3f * scale));
            }
        if (impactBrush != null)
            foreach (BulletImpact impact in combat.Impacts)
            {
                float life = impact.Age / CombatSystem.ImpactLifetime;
                impactBrush.Color = impact.HitZombie
                    ? new Color4(0.95f, 0.25f, 0.15f, 1f - life)
                    : new Color4(1f, 0.85f, 0.45f, 1f - life);
                Vector2 center = ToScreen(impact.Position);
                for (int ray = 0; ray < 6; ray++)
                {
                    float angle = ray * MathF.PI / 3;
                    Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                    RenderTarget.DrawLine(center + direction * (2 + life * 5) * scale,
                        center + direction * (7 + life * 12) * scale, impactBrush, 2f);
                }
            }
    }

    private void DisposeCombat()
    {
        bulletBrush?.Dispose();
        impactBrush?.Dispose();
        combat.Reset();
        WindowCursor = System.Windows.Forms.Cursors.Default;
    }





    private readonly EnemyAttackSystem enemyAttacks = new();
    private readonly G2Texture?[,] rangedZombieImages = new G2Texture?[2, 4];
    private ID2D1SolidColorBrush? acidBrush;

    private void InitializeRangedEnemies()
    {
        for (int level = 0; level < 2; level++)
        for (int frame = 0; frame < 4; frame++)
            rangedZombieImages[level, frame] = new G2Texture(Path.Combine(playImageFolder, "zombies", "walking",
                $"ranged_lv{level + 1}_{frame}.png"));
        acidBrush = RenderTarget.CreateSolidColorBrush(new Color4(0.6f, 1f, 0.15f, 1f));
    }

    private void DrawEnemyAttacks(float scale, float offsetX, float offsetY)
    {
        if (acidBrush == null) return;
        Vector2 ToScreen(Vector2 p) => new(offsetX + p.X * scale, offsetY + p.Y * scale);
        foreach (ZombieEnemy zombie in zombieWaves.Zombies)
        {
            if (!zombie.IsRanged || zombie.AttackWindup <= 0) continue;
            Vector2 mouth = ToScreen(zombie.AttackOrigin);
            float charge = 1f - zombie.AttackWindup / EnemyAttackSystem.WindupSeconds;
            acidBrush.Color = new Color4(0.7f, 1f, 0.2f, 0.6f + charge * 0.4f);
            RenderTarget.DrawEllipse(new Ellipse(mouth, (8 + charge * 6) * scale,
                (8 + charge * 6) * scale), acidBrush, 2f);
        }
        foreach (AcidShot shot in enemyAttacks.Shots)
        {
            Vector2 center = ToScreen(shot.Position);
            acidBrush.Color = new Color4(0.35f, 0.8f, 0.05f, 0.6f);
            RenderTarget.DrawLine(ToScreen(shot.Position - shot.Direction * 24f), center, acidBrush, 8f * scale);
            acidBrush.Color = new Color4(0.6f, 1f, 0.15f, 1f);
            RenderTarget.FillEllipse(new Ellipse(center, EnemyAttackSystem.ShotRadius * scale,
                EnemyAttackSystem.ShotRadius * scale), acidBrush);
            acidBrush.Color = new Color4(0.93f, 1f, 0.65f, 1f);
            RenderTarget.FillEllipse(new Ellipse(center - new Vector2(2f, 2f), 3f * scale, 3f * scale), acidBrush);
        }
    }

    private void DisposeRangedEnemies()
    {
        foreach (G2Texture? image in rangedZombieImages) image?.Dispose();
        acidBrush?.Dispose();
        enemyAttacks.Reset();
    }





    private G2Texture? gameOverImage;
    private Rect gameOverSource;
    private G2Font? resultLabelFont;
    private G2Font? resultValueFont;
    private G2Font? resultHintFont;
    private int resultHover;
    private int resultCapture;
    private bool resultMouseWasDown;
    private bool resultMouseArmed;

    private void InitializeGameOver()
    {
        string path = Path.Combine(playImageFolder, "..", "img_ui", "game_over_background.png");
        gameOverImage = new G2Texture(path);
        using (var bitmap = Image.FromFile(path))
            gameOverSource = new Rect(0, 0, bitmap.Width, bitmap.Height);
        resultLabelFont = new G2Font("Consolas", 12f, textAlignment: Vortice.DirectWrite.TextAlignment.Center);
        resultValueFont = new G2Font("Consolas", 29f, textAlignment: Vortice.DirectWrite.TextAlignment.Center);
        resultHintFont = new G2Font("Malgun Gothic", 12f, textAlignment: Vortice.DirectWrite.TextAlignment.Center);
    }

    // 그림의 1536x1024 기준 배치를 화면 논리 좌표로 환산합니다.
    private RectangleF ResultRect(float x, float y, float width, float height) =>
        new(x * ScreenSize.Width / 1536f, y * ScreenSize.Height / 1024f,
            width * ScreenSize.Width / 1536f, height * ScreenSize.Height / 1024f);

    private RectangleF ResultButton(int button)
    {
        // 메인 메뉴와 같은 크기와 가로 위치를 쓰고 결과 화면에 맞춰 세로 위치만 옮깁니다.
        GetMenuLayout(out _, out RectangleF start, out RectangleF quit);
        RectangleF result = button == 1 ? start : quit;
        result.Y = (button == 1 ? 390f : 500f) * ScreenSize.Height / MenuHeight;
        return result;
    }

    private void ResetGameOverInput()
    {
        resultHover = resultCapture = 0;
        resultMouseWasDown = false;
        resultMouseArmed = false;
    }

    private void ReturnToMainMenu()
    {
        isPlaying = false;
        isPaused = false;
        startCaptured = quitCaptured = startIsPressed = quitIsPressed = false;
        mouseWasDown = KeyDown(0x01);
        ResetGameOverInput();
        UpdateGameCursor();
    }

    private void ProcessGameOverMouse(bool valid, bool down, Vector2 point)
    {
        if (!isPlaying || !playerSurvival.IsGameOver) return;
        if (!valid)
        {
            ResetGameOverInput();
            return;
        }
        resultHover = ResultButton(1).Contains(point.X, point.Y) ? 1
            : ResultButton(2).Contains(point.X, point.Y) ? 2 : 0;
        // 사격 버튼을 누른 채 사망해도 게임이 바로 재시작되지 않도록 먼저 떼어야 합니다.
        if (!resultMouseArmed)
        {
            resultMouseArmed = !down;
            resultMouseWasDown = down;
            return;
        }
        if (down && !resultMouseWasDown) resultCapture = resultHover;
        if (!down && resultMouseWasDown)
        {
            int selected = resultCapture == resultHover ? resultCapture : 0;
            resultCapture = 0;
            if (selected == 1) { StartGame(); return; }
            if (selected == 2) { ReturnToMainMenu(); return; }
        }
        resultMouseWasDown = down;
    }

    private void RenderGameOver()
    {
        gameOverImage?.Draw(new Rect(0, 0, ScreenSize.Width, ScreenSize.Height), gameOverSource);
        int seconds = (int)zombieWaves.ElapsedSeconds;
        string[] labels = { "SURVIVED", "KILLS", "STAGE" };
        string[] values = { $"{seconds / 60:00}:{seconds % 60:00}", zombieWaves.KillCount.ToString(), zombieWaves.Stage.ToString() };
        for (int column = 0; column < 3; column++)
        {
            RectangleF label = ResultRect(390 + column * 252, 480, 252, 30);
            RectangleF value = ResultRect(390 + column * 252, 514, 252, 68);
            resultLabelFont?.DrawText(labels[column], new Rect(label.X, label.Y, label.Width, label.Height), new Color4(0.71f, 0.64f, 0.54f, 1));
            resultValueFont?.DrawText(values[column], new Rect(value.X, value.Y, value.Width, value.Height), new Color4(1f, 0.91f, 0.77f, 1));
        }
        for (int button = 1; button <= 2; button++)
        {
            RectangleF bounds = ResultButton(button);
            bool pressed = resultCapture == button && resultHover == button && resultMouseWasDown;
            G2Texture? texture = button == 1 ? startImage : quitImage;
            Rect source = button == 1 ? (pressed ? startPressed : startNormal)
                : (pressed ? quitPressed : quitNormal);
            texture?.Draw(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), source);
        }
        resultHintFont?.DrawText("START 다시 시작  ·  QUIT 메인 메뉴     |     R / Enter 재시작  ·  ESC 메뉴",
            new Rect(0, ScreenSize.Height - 29, ScreenSize.Width, 24), new Color4(0.76f, 0.73f, 0.68f, 1));
    }

    private void DisposeGameOver()
    {
        gameOverImage?.Dispose();
        resultLabelFont?.Dispose();
        resultValueFont?.Dispose();
        resultHintFont?.Dispose();
    }





    private G2AudioSound? introMusic;
    private float introMusicVolume;
    private const float IntroMusicVolume = 0.65f;

    private void InitializeIntroMusic(string resourcePath)
    {
        // A machine without an audio output can still run the game.
        if (G2AudioContext.Instance?.Audio == null) return;
        try
        {
            introMusic = new G2AudioSound(Path.Combine(resourcePath, "audio", "dead_zone_intro.wav"));
            introMusic.SetVolume(0);
        }
        catch (Exception ex)
        {
            introMusic?.Dispose();
            introMusic = null;
            Debug.WriteLine($"Intro music unavailable: {ex.Message}");
        }
    }

    private void UpdateIntroMusic(float dt)
    {
        if (introMusic == null) return;
        float target = !isPlaying && IsGameFocused() ? IntroMusicVolume : 0;
        if (target > 0 && !introMusic.IsPlaying()) introMusic.Play(isLooping: true);

        // One-second fade in, 0.65-second fade out on START or focus loss.
        float step = IntroMusicVolume * dt / (target > introMusicVolume ? 1f : 0.65f);
        introMusicVolume = target > introMusicVolume
            ? Math.Min(target, introMusicVolume + step)
            : Math.Max(target, introMusicVolume - step);
        introMusic.SetVolume(introMusicVolume);
        if (introMusicVolume == 0 && introMusic.IsPlaying()) introMusic.Stop();
    }


}



internal enum ZombieKind { Normal, Fast, Brute, Ranged, RangedAdvanced }
internal enum SpawnEdge { Top, Right, Bottom, Left }

internal sealed class ZombieEnemy
{
    public ZombieKind Kind { get; }
    public bool IsRanged => Kind is ZombieKind.Ranged or ZombieKind.RangedAdvanced;
    public int RangedLevel => Kind == ZombieKind.RangedAdvanced ? 2 : 1;
    public int MaximumHealth => Kind == ZombieKind.Brute ? 3 : 1;
    public int Health { get; internal set; }
    public SpawnEdge Edge { get; }
    public Vector2 SpawnPosition { get; }
    public Vector2 Position { get; internal set; }
    public float WalkTime { get; internal set; }
    public bool IsMoving { get; internal set; }
    public int SteerSign { get; }
    public float AttackCooldown { get; internal set; } = 1.2f;
    public float AttackWindup { get; internal set; }
    public Vector2 AttackDirection { get; internal set; }
    public Vector2 AttackOrigin => Position + new Vector2(0, (Kind == ZombieKind.RangedAdvanced ? -50f : -44f) * VisualScale);
    public const float VisualScale = 1.15f;
    public float Radius => RadiusFor(Kind);
    public RectangleF HitBounds
    {
        get
        {
            float width = (Kind == ZombieKind.Brute ? 58f : Kind is ZombieKind.Fast or ZombieKind.RangedAdvanced ? 40f : 28f) * VisualScale;
            float height = (Kind == ZombieKind.Brute ? 80f : Kind is ZombieKind.Fast or ZombieKind.RangedAdvanced ? 70f : 62f) * VisualScale;
            return new RectangleF(Position.X - width / 2, Position.Y - height, width, height);
        }
    }
    public static float RadiusFor(ZombieKind kind) => (kind == ZombieKind.Brute ? 20f : 14f) * VisualScale;
    // 실제 이동 거리에 맞춰 발걸음을 진행하므로 빠른 좀비도 미끄러지지 않습니다.
    public int WalkFrame => IsMoving ? (int)(WalkTime / 0.14f) % 4 : 0;
    public float BaseSpeed => Kind switch
    {
        ZombieKind.Fast => 150f,
        ZombieKind.Brute => 58f,
        ZombieKind.Ranged or ZombieKind.RangedAdvanced => 60f,
        _ => 50f
    };

    public ZombieEnemy(ZombieKind kind, SpawnEdge edge, Vector2 position, int steerSign)
    {
        Kind = kind;
        Health = MaximumHealth;
        Edge = edge;
        SpawnPosition = Position = position;
        SteerSign = steerSign;
        WalkTime = (MathF.Abs(position.X) * 0.017f + MathF.Abs(position.Y) * 0.013f) % 0.56f;
    }
}

// 렌더링/키보드와 분리한 웨이브 및 추적 로직. 좌표는 좀비의 발밑 기준입니다.
internal sealed class ZombieWaveSystem
{
    public const double SpawnInterval = 3.0;
    public const int ZombiesPerWave = 5;
    private const float GridSize = 64f;
    private readonly Random random;
    private readonly List<ZombieEnemy> zombies = new();
    private readonly Dictionary<(int X, int Y), List<ZombieEnemy>> nearby = new();
    private int waveCount;

    public IReadOnlyList<ZombieEnemy> Zombies => zombies;
    public int KillCount { get; private set; }
    public bool Hit(ZombieEnemy zombie)
    {
        if (zombie.Health <= 0 || !zombies.Contains(zombie)) return false;
        zombie.Health--;
        if (zombie.Health == 0) Kill(zombie);
        return true;
    }

    public bool Kill(ZombieEnemy zombie)
    {
        if (!zombies.Remove(zombie)) return false;
        zombie.Health = 0;
        KillCount++;
        return true;
    }
    public double ElapsedSeconds { get; private set; }
    public int Stage => StageAt(ElapsedSeconds);
    public float SpeedMultiplier => SpeedAt(ElapsedSeconds);

    public ZombieWaveSystem(int? seed = null)
    {
        random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public static int StageAt(double seconds) => seconds < 20 ? 1 : seconds < 40 ? 2 : seconds < 60 ? 3 : 4;

    internal static ZombieKind KindAt(double spawnSeconds, int slot) =>
        spawnSeconds >= 120 && slot == 2 ? ZombieKind.RangedAdvanced :
        spawnSeconds > 100 && slot == 2 ? ZombieKind.Ranged :
        spawnSeconds > 80 && slot == 1 ? ZombieKind.Ranged :
        spawnSeconds >= 60 && slot == 4 ? ZombieKind.Brute :
        spawnSeconds >= 20 && slot == 0 ? ZombieKind.Fast :
        spawnSeconds >= 40 && slot == 2 ? ZombieKind.Fast : ZombieKind.Normal;

    public static float SpeedAt(double seconds) => StageAt(seconds) switch
    {
        1 => 1f,
        2 => 1.2f,
        3 => 1.4f,
        // 장시간 실행 시에도 한 프레임 이동량을 안정적으로 유지합니다.
        _ => 1.5f + 1.5f * (1f - MathF.Exp(-(float)(seconds - 60) / 120f))
    };

    public void Reset()
    {
        ElapsedSeconds = 0;
        KillCount = 0;
        waveCount = 0;
        zombies.Clear();
        nearby.Clear();
    }

    public void Update(float dt, RectangleF viewport, Vector2 playerFeet, IReadOnlyList<RectangleF> blockers)
    {
        if (!float.IsFinite(dt) || dt <= 0)
            return;

        ElapsedSeconds += dt;
        while (ElapsedSeconds + 1e-9 >= (waveCount + 1) * SpawnInterval)
        {
            double spawnSeconds = (waveCount + 1) * SpawnInterval;
            SpawnWave(viewport, playerFeet, blockers, spawnSeconds);
            waveCount++;
        }

        nearby.Clear();
        foreach (ZombieEnemy zombie in zombies)
        {
            var key = Cell(zombie.Position);
            if (!nearby.TryGetValue(key, out var group))
                nearby[key] = group = new List<ZombieEnemy>();
            group.Add(zombie);
        }

        foreach (ZombieEnemy zombie in zombies)
            MoveZombie(zombie, dt, playerFeet, blockers, viewport);
    }

    private void SpawnWave(RectangleF view, Vector2 player, IReadOnlyList<RectangleF> blockers, double spawnSeconds)
    {
        for (int index = 0; index < ZombiesPerWave; index++)
        {
            // 4방향에 한 마리씩 + 추가 한 마리의 방향은 매 웨이브 순환합니다.
            SpawnEdge edge = (SpawnEdge)((waveCount + index) % 4);
            ZombieKind kind = KindAt(spawnSeconds, index);
            float radius = ZombieEnemy.RadiusFor(kind);
            Vector2 best = default;
            float bestScore = float.NegativeInfinity;
            bool found = false;
            // 도로 입구 안에서 건물/기존 좀비를 피하고 플레이어와 거리를 둡니다.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float lane = random.NextSingle();
                Vector2 candidate = edge switch
                {
                    SpawnEdge.Top => new(view.Left + view.Width * (0.36f + lane * 0.28f), view.Top - 10f),
                    SpawnEdge.Bottom => new(view.Left + view.Width * (0.36f + lane * 0.28f), view.Bottom + 90f),
                    SpawnEdge.Left => new(view.Left - 50f, view.Top + view.Height * (0.30f + lane * 0.35f)),
                    _ => new(view.Right + 50f, view.Top + view.Height * (0.30f + lane * 0.35f))
                };
                if (IntersectsBlock(candidate, radius, blockers))
                    continue;
                float separation = float.MaxValue;
                foreach (ZombieEnemy other in zombies)
                    separation = Math.Min(separation, Vector2.DistanceSquared(candidate, other.Position));
                float playerDistance = Vector2.DistanceSquared(candidate, player);
                float score = Math.Min(playerDistance, 40000f) + Math.Min(separation, 10000f);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                    found = true;
                }
                if (playerDistance >= 120f * 120f && separation >= 48f * 48f)
                    break;
            }
            // 현재 맵의 네 도로 입구에는 항상 유효 지점이 있습니다.
            if (found)
                zombies.Add(new ZombieEnemy(kind, edge, best, (waveCount + index) % 2 == 0 ? 1 : -1));
        }
    }

    private void MoveZombie(ZombieEnemy zombie, float dt, Vector2 player, IReadOnlyList<RectangleF> blockers, RectangleF viewport)
    {
        Vector2 oldPosition = zombie.Position;
        if (zombie.IsRanged && viewport.Contains(zombie.AttackOrigin.X, zombie.AttackOrigin.Y) &&
            (zombie.AttackWindup > 0 || (Vector2.DistanceSquared(zombie.Position, player) <= 260f * 260f &&
            EnemyAttackSystem.ClearShot(zombie.AttackOrigin, player + new Vector2(0, -26), blockers))))
        {
            zombie.IsMoving = false;
            return;
        }
        float distance = zombie.BaseSpeed * SpeedMultiplier * dt;
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / 4f));
        float stepLength = distance / steps;
        for (int step = 0; step < steps; step++)
        {
            Vector2 delta = player - zombie.Position;
            if (delta.LengthSquared() <= MathF.Pow(zombie.Radius + 15f, 2))
                break;
            Vector2 direction = Vector2.Normalize(delta);
            // 정면이 막히면 양옆으로 돌아갑니다. 픽셀 단위로 검사해 벽 관통을 막습니다.
            for (int turn = 0; turn < 7; turn++)
            {
                float angle = turn == 0 ? 0f
                    : ((turn + 1) / 2) * (MathF.PI / 6f) * (turn % 2 == 1 ? zombie.SteerSign : -zombie.SteerSign);
                float cosine = MathF.Cos(angle);
                float sine = MathF.Sin(angle);
                Vector2 steered = new(direction.X * cosine - direction.Y * sine,
                    direction.X * sine + direction.Y * cosine);
                Vector2 next = zombie.Position + steered * stepLength;
                if (IntersectsBlock(next, zombie.Radius, blockers) || Crowded(zombie, next))
                    continue;
                if (Vector2.DistanceSquared(next, player) < MathF.Pow(zombie.Radius + 15f, 2))
                    continue;
                zombie.Position = next;
                break;
            }
        }
        zombie.IsMoving = Vector2.DistanceSquared(oldPosition, zombie.Position) > 0.001f;
        if (zombie.IsMoving)
            zombie.WalkTime += Vector2.Distance(oldPosition, zombie.Position) / 72f;
    }

    private bool Crowded(ZombieEnemy zombie, Vector2 next)
    {
        var cell = Cell(next);
        for (int y = cell.Y - 1; y <= cell.Y + 1; y++)
        for (int x = cell.X - 1; x <= cell.X + 1; x++)
        {
            if (!nearby.TryGetValue((x, y), out var group))
                continue;
            foreach (ZombieEnemy other in group)
            {
                if (ReferenceEquals(zombie, other))
                    continue;
                float nextDistance = Vector2.DistanceSquared(next, other.Position);
                float minimum = zombie.Radius + other.Radius;
                if (nextDistance < minimum * minimum &&
                    nextDistance < Vector2.DistanceSquared(zombie.Position, other.Position))
                    return true;
            }
        }
        return false;
    }

    private static (int X, int Y) Cell(Vector2 position) =>
        ((int)MathF.Floor(position.X / GridSize), (int)MathF.Floor(position.Y / GridSize));

    internal static bool IntersectsBlock(Vector2 feet, float radius, IReadOnlyList<RectangleF> blockers)
    {
        foreach (RectangleF block in blockers)
        {
            float dx = feet.X - Math.Clamp(feet.X, block.Left, block.Right);
            float dy = feet.Y - Math.Clamp(feet.Y, block.Top, block.Bottom);
            if (dx * dx + dy * dy < radius * radius)
                return true;
        }
        return false;
    }
}




internal sealed class PlayerSurvival
{
    public const int MaximumHealth = 3;
    public const float InvulnerabilitySeconds = 1.25f;
    public int Health { get; private set; } = MaximumHealth;
    public float InvulnerabilityRemaining { get; private set; }
    public bool IsGameOver => Health == 0;

    public bool TryTakeHit()
    {
        if (IsGameOver || InvulnerabilityRemaining > 0) return false;
        Health--;
        InvulnerabilityRemaining = IsGameOver ? 0 : InvulnerabilitySeconds;
        return true;
    }

    public void Reset()
    {
        Health = MaximumHealth;
        InvulnerabilityRemaining = 0;
    }

    public void Update(float dt, Vector2 playerFeet, IReadOnlyList<ZombieEnemy> zombies)
    {
        if (IsGameOver || !float.IsFinite(dt) || dt <= 0) return;
        InvulnerabilityRemaining = Math.Max(0, InvulnerabilityRemaining - dt);
        if (InvulnerabilityRemaining > 0) return;
        foreach (ZombieEnemy zombie in zombies)
        {
            // 추적 이동이 발밑 충돌 원 직전에 멈추므로 최대 이동 소단계(4px)를 포함합니다.
            float contactDistance = zombie.Radius + 15f + 4f;
            if (Vector2.DistanceSquared(zombie.Position, playerFeet) > contactDistance * contactDistance)
                continue;
            TryTakeHit();
            break; // 여러 좀비가 동시에 닿아도 한 번만 피격됩니다.
        }
    }
}





internal sealed class Bullet
{
    public Vector2 Position { get; internal set; }
    public Vector2 Direction { get; }
    public float Age { get; internal set; }
    public Bullet(Vector2 position, Vector2 direction) { Position = position; Direction = direction; }
}

internal sealed class BulletImpact
{
    public Vector2 Position { get; }
    public bool HitZombie { get; }
    public float Age { get; internal set; }
    public BulletImpact(Vector2 position, bool hitZombie) { Position = position; HitZombie = hitZombie; }
}

internal sealed class CombatSystem
{
    public const float FireInterval = 0.16f;
    public const float BulletSpeed = 900f;
    public const float BulletLifetime = 1.8f;
    public const float ImpactLifetime = 0.2f;
    private readonly List<Bullet> bullets = new();
    private readonly List<BulletImpact> impacts = new();
    private float cooldown;
    public IReadOnlyList<Bullet> Bullets => bullets;
    public IReadOnlyList<BulletImpact> Impacts => impacts;

    public void Reset() { bullets.Clear(); impacts.Clear(); cooldown = 0; }

    public void Update(float dt, bool trigger, Vector2 origin, Vector2 muzzle, Vector2 aim,
        ZombieWaveSystem waves, IReadOnlyList<RectangleF> blockers)
    {
        if (!float.IsFinite(dt) || dt <= 0) return;
        cooldown = Math.Max(0, cooldown - dt);
        for (int i = impacts.Count - 1; i >= 0; i--)
        {
            impacts[i].Age += dt;
            if (impacts[i].Age >= ImpactLifetime) impacts.RemoveAt(i);
        }
        Vector2 delta = aim - muzzle;
        if (trigger && cooldown <= 0 && float.IsFinite(delta.X) && float.IsFinite(delta.Y) && delta.LengthSquared() > 0.01f)
        {
            cooldown = FireInterval;
            // 총구가 벽/좀비 너머에 놓여도 발사 직전 구간의 충돌을 생략하지 않습니다.
            if (!ResolveImpact(origin, muzzle, waves, blockers))
                bullets.Add(new Bullet(muzzle, Vector2.Normalize(delta)));
        }
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            Bullet bullet = bullets[i];
            float travelTime = Math.Min(dt, BulletLifetime - bullet.Age);
            Vector2 next = bullet.Position + bullet.Direction * BulletSpeed * travelTime;
            bool hit = ResolveImpact(bullet.Position, next, waves, blockers);
            bullet.Position = next;
            bullet.Age += dt;
            if (hit || bullet.Age >= BulletLifetime) bullets.RemoveAt(i);
        }
    }

    private bool ResolveImpact(Vector2 from, Vector2 to, ZombieWaveSystem waves, IReadOnlyList<RectangleF> blockers)
    {
        float closest = float.PositiveInfinity;
        ZombieEnemy? target = null;
        foreach (RectangleF block in blockers)
            if (SegmentHit(from, to, block, out float fraction) && fraction < closest) closest = fraction;
        foreach (ZombieEnemy zombie in waves.Zombies)
        {
            if (!SegmentHit(from, to, zombie.HitBounds, out float fraction) || fraction >= closest) continue;
            closest = fraction;
            target = zombie;
        }
        if (!float.IsFinite(closest)) return false;
        if (target != null) waves.Hit(target);
        impacts.Add(new BulletImpact(Vector2.Lerp(from, to, closest), target != null));
        return true;
    }

    // 이동 선분 전체를 검사해 빠른 총알이 얇은 벽이나 적을 통과하지 않게 합니다.
    internal static bool SegmentHit(Vector2 from, Vector2 to, RectangleF rectangle, out float fraction)
    {
        Vector2 delta = to - from;
        float enter = 0, exit = 1;
        bool hit = Slab(from.X, delta.X, rectangle.Left, rectangle.Right, ref enter, ref exit) &&
            Slab(from.Y, delta.Y, rectangle.Top, rectangle.Bottom, ref enter, ref exit);
        fraction = enter;
        return hit;
    }

    private static bool Slab(float start, float delta, float minimum, float maximum, ref float enter, ref float exit)
    {
        if (MathF.Abs(delta) < 0.00001f) return start >= minimum && start <= maximum;
        float a = (minimum - start) / delta, b = (maximum - start) / delta;
        if (a > b) (a, b) = (b, a);
        enter = Math.Max(enter, a);
        exit = Math.Min(exit, b);
        return enter <= exit;
    }
}





internal sealed class AcidShot
{
    public Vector2 Position { get; internal set; }
    public Vector2 Direction { get; }
    public float Age { get; internal set; }
    public AcidShot(Vector2 position, Vector2 direction) { Position = position; Direction = direction; }
}

internal sealed class EnemyAttackSystem
{
    public const float ShotSpeed = 240f;
    public const float ShotRadius = 9f;
    public const float ShotLifetime = 3f;
    public const float AttackRange = 420f;
    public const float WindupSeconds = 0.45f;
    public const float AttackInterval = 2.2f;
    private readonly List<AcidShot> shots = new();
    public IReadOnlyList<AcidShot> Shots => shots;
    public void Reset() => shots.Clear();

    public void Update(float dt, IReadOnlyList<ZombieEnemy> zombies, Vector2 playerFeet,
        RectangleF viewport, IReadOnlyList<RectangleF> blockers, PlayerSurvival survival)
    {
        if (!float.IsFinite(dt) || dt <= 0 || survival.IsGameOver) return;
        var playerBody = new RectangleF(playerFeet.X - 13, playerFeet.Y - 64, 26, 64);
        playerBody.Inflate(ShotRadius, ShotRadius);
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            AcidShot shot = shots[i];
            Vector2 next = shot.Position + shot.Direction * ShotSpeed * Math.Min(dt, ShotLifetime - shot.Age);
            float wallHit = float.PositiveInfinity;
            foreach (RectangleF obstacle in blockers)
            {
                RectangleF padded = obstacle;
                padded.Inflate(ShotRadius, ShotRadius);
                if (CombatSystem.SegmentHit(shot.Position, next, padded, out float fraction)) wallHit = Math.Min(wallHit, fraction);
            }
            bool hitPlayer = CombatSystem.SegmentHit(shot.Position, next, playerBody, out float playerHit) && playerHit < wallHit;
            if (hitPlayer) survival.TryTakeHit();
            shot.Position = next;
            shot.Age += dt;
            if (hitPlayer || float.IsFinite(wallHit) || shot.Age >= ShotLifetime) shots.RemoveAt(i);
        }
        if (survival.IsGameOver) return;
        Vector2 target = playerFeet + new Vector2(0, -26);
        foreach (ZombieEnemy zombie in zombies)
        {
            if (!zombie.IsRanged) continue;
            Vector2 origin = zombie.AttackOrigin;
            Vector2 delta = target - origin;
            bool canAttack = viewport.Contains(origin.X, origin.Y) && delta.LengthSquared() > 1f &&
                delta.LengthSquared() <= AttackRange * AttackRange && ClearShot(origin, target, blockers);
            if (!canAttack)
            {
                zombie.AttackWindup = 0;
                zombie.AttackCooldown = Math.Max(0.3f, zombie.AttackCooldown - dt);
                continue;
            }
            if (zombie.AttackWindup > 0)
            {
                zombie.AttackWindup = Math.Max(0, zombie.AttackWindup - dt);
                if (zombie.AttackWindup == 0)
                {
                    // 예고를 시작한 위치로 발사하므로 옆으로 움직여 피할 수 있습니다.
                    int count = zombie.RangedLevel == 2 ? 2 : 1;
                    Vector2 perpendicular = new(-zombie.AttackDirection.Y, zombie.AttackDirection.X);
                    for (int shot = 0; shot < count; shot++)
                    {
                        float side = count == 1 ? 0 : shot == 0 ? -1 : 1;
                        float angle = side * 0.08f;
                        Vector2 direction = new(
                            zombie.AttackDirection.X * MathF.Cos(angle) - zombie.AttackDirection.Y * MathF.Sin(angle),
                            zombie.AttackDirection.X * MathF.Sin(angle) + zombie.AttackDirection.Y * MathF.Cos(angle));
                        Vector2 launch = origin + perpendicular * side * 11f;
                        if (ClearShot(origin, launch, blockers))
                            shots.Add(new AcidShot(launch, direction));
                    }
                    zombie.AttackCooldown = AttackInterval;
                }
            }
            else
            {
                zombie.AttackCooldown = Math.Max(0, zombie.AttackCooldown - dt);
                if (zombie.AttackCooldown == 0)
                {
                    zombie.AttackDirection = Vector2.Normalize(delta);
                    zombie.AttackWindup = WindupSeconds;
                }
            }
        }
    }

    internal static bool ClearShot(Vector2 origin, Vector2 target, IReadOnlyList<RectangleF> blockers)
    {
        foreach (RectangleF block in blockers)
        {
            RectangleF padded = block;
            padded.Inflate(ShotRadius, ShotRadius);
            if (CombatSystem.SegmentHit(origin, target, padded, out _)) return false;
        }
        return true;
    }
}


