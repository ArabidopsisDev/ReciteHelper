using ReciteHelper.Model;
using System.Text.Json;
using System.Windows;

namespace ReciteHelper.Utils;

public class OCRProvider
{
    private AlibabaCloud.SDK.Ocr_api20210707.Client? CreateValidation()
    {
        if (ReciteHelper.Model.Config.Configure is null ||
             ReciteHelper.Model.Config.Configure.OCRAccess is null ||
              ReciteHelper.Model.Config.Configure.OCRSecret is null)
            return null;

        var config = new Aliyun.Credentials.Models.Config()
        {
            Type = "access_key",
            AccessKeyId = ReciteHelper.Model.Config.Configure.OCRAccess,
            AccessKeySecret = ReciteHelper.Model.Config.Configure.OCRSecret
        };
        var credentialClient = new Aliyun.Credentials.Client(config);

        var conf = new AlibabaCloud.OpenApiClient.Models.Config
        {
            Credential = credentialClient,
        };
        conf.Endpoint = "ocr-api.cn-hangzhou.aliyuncs.com";
        var client = new AlibabaCloud.SDK.Ocr_api20210707.Client(conf);

        return client;
    }

    public string? Request()
    {
        var client = CreateValidation();

        if (client is null)
        {
            MessageBox.Show("您还未配置阿里云API...", "提示",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var bodyStream = AlibabaCloud.DarabonbaStream.StreamUtil.ReadFromFilePath(@"C:\Users\Arabid\Desktop\test.png");
        var recognizeGeneralRequest = new AlibabaCloud.SDK.Ocr_api20210707.Models.RecognizeGeneralRequest
        {
            Body = bodyStream,
        };
        var runtime = new AlibabaCloud.TeaUtil.Models.RuntimeOptions();

        var result = client.RecognizeGeneralWithOptions(recognizeGeneralRequest, runtime);
        var data = JsonSerializer.Deserialize<Text>(result.Body.Data);

        if (data is null) return null;
        return data.Content;
    }
}
