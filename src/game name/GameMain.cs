
using Vortice.Mathematics;
using System.Drawing;

class GameMain : G2AppBase
{
    public override System.Drawing.Size ScreenSize => GameGlobal.ScreenSize;
    public override string GameName => GameGlobal.GameName;

    private G2Texture? mainImage;
    private float imageWidth;
    private float imageHeight;

    protected override void Initialize()
    {
        string imagePath =
            @"C:\Users\Windows11\26031001_choisea_GameCraft\src\game name\resource\img_ui\main.png";

        mainImage = new G2Texture(imagePath);

        // 원본 이미지 크기 가져오기
        using (var image = Image.FromFile(imagePath))
        {
            imageWidth = image.Width;
            imageHeight = image.Height;
        }
    }

    protected override void Update()
    {
        ClearColor = new Color4(0f, 0f, 0f, 1f);
    }

    protected override void Render()
    {
        if (mainImage == null)
            return;

        // 화면 크기에 맞게 비율 계산
        float scaleX = (float)ScreenSize.Width / imageWidth;
        float scaleY = (float)ScreenSize.Height / imageHeight;

        // 이미지가 잘리지 않도록 작은 배율 선택
        float scale = Math.Min(scaleX, scaleY);

        float width = imageWidth * scale;
        float height = imageHeight * scale;

        // 화면 중앙 정렬
        float x = (ScreenSize.Width - width) / 2f;
        float y = (ScreenSize.Height - height) / 2f;

        var destination = new Rect(x, y, width, height);
        var source = new Rect(0, 0, imageWidth, imageHeight);

        mainImage.Draw(destination, source);
    }

    public override void Dispose()
    {
        mainImage?.Dispose();
        base.Dispose();
    }
}
