
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
            }
        }

        // 건물 및 장애물 생성
        CreateBuildings();
        CreateObstacles();

        // 플레이어 초기화
        ResetPlayer();

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

        // 왼쪽 차 - 조금 더 아래로 + 크기 증가
        AddObstacle(
            "green_car.png",
            110, 325,
            230, 190,
            0.10f, 0.50f,
            0.82f, 0.35f);

        // 오른쪽 차 - 조금 더 위로 + 크기 증가
        AddObstacle(
            "car.png",
            1345, 250,
            235, 190,
            0.10f, 0.48f,
            0.82f, 0.37f);

        // 초록 쓰레기 컨테이너 - 크기 증가
        AddObstacle(
            "green_box.png",
            400, 730,
            150, 130,
            0.18f, 0.65f,
            0.67f, 0.26f);

        // 나무 박스 - 크기 증가
        AddObstacle(
            "Box.png",
            700, 640,
            92, 92,
            0.17f, 0.60f,
            0.66f, 0.30f);

        // 신호등 - 크기 증가
        AddObstacle(
            "signal.png",
            1050, 38,
            82, 162,
            0.32f, 0.82f,
            0.27f, 0.13f);

        // 쓰레기통 - 크기 증가
        AddObstacle(
            "trash.png",
            1275, 570,
            78, 88,
            0.24f, 0.65f,
            0.60f, 0.25f);

        // 꼬깔콘 1 - 크기 증가
        AddObstacle(
            "Kkokka.png",
            570, 338,
            50, 70,
            0.31f, 0.74f,
            0.39f, 0.19f);

        // 꼬깔콘 2 - 크기 증가
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

        mouseX = point.X;
        mouseY = point.Y;

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

        if (isPlaying)
        {
            UpdatePlayer(dt);
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
                Environment.Exit(0);
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

        isWalking = false;
        walkTimer = 0f;
    }

    // 게임 시작
    private void StartGame()
    {
        ResetPlayer();
        isPlaying = true;
    }

    // 플레이어 이동
    private void UpdatePlayer(float dt)
    {
        if (!IsGameFocused())
        {
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

        Direction nextDirection = facing;

        // 상하좌우 이동
        // 대각선 이동 불가능
        if (up && !down)
        {
            dy = -1f;
            nextDirection = Direction.Up;
        }
        else if (down && !up)
        {
            dy = 1f;
            nextDirection = Direction.Down;
        }
        else if (left && !right)
        {
            dx = -1f;
            nextDirection = Direction.Left;
        }
        else if (right && !left)
        {
            dx = 1f;
            nextDirection = Direction.Right;
        }

        // 이동하지 않는 경우
        if (dx == 0f && dy == 0f)
        {
            isWalking = false;
            walkTimer = 0f;
            return;
        }

        // 방향 변경
        if (facing != nextDirection)
        {
            walkTimer = 0f;
        }

        facing = nextDirection;

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

        G2Texture? texture =
            playerImages[(int)facing, frame];

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
                96,
                96));
    }

    // 게임 화면 그리기
    private void RenderGame()
    {
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

        // 2. 캐릭터와 장애물 깊이 정렬
        float playerFeetY =
            playerY + FeetOffsetY;

        bool playerDrawn = false;

        foreach (Obstacle obstacle in obstacles)
        {
            // 캐릭터가 장애물 뒤에 있으면
            // 먼저 캐릭터를 그려서 가려지게 처리
            if (!playerDrawn &&
                playerFeetY < obstacle.SortY)
            {
                DrawPlayer(
                    scale,
                    offsetX,
                    offsetY);

                playerDrawn = true;
            }

            // 장애물 출력
            DrawObstacle(
                obstacle,
                scale,
                offsetX,
                offsetY);
        }

        // 아직 캐릭터를 그리지 않았다면
        // 모든 장애물보다 앞쪽에 출력
        if (!playerDrawn)
        {
            DrawPlayer(
                scale,
                offsetX,
                offsetY);
        }
    }

    // 리소스 해제
    public override void Dispose()
    {
        mainImage?.Dispose();
        startImage?.Dispose();
        quitImage?.Dispose();
        stageImage?.Dispose();

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
}
