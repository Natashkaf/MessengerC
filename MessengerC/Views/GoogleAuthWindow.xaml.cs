// окно авторизации через гугл 
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using System.Text.Json;

namespace MessengerApp
{
    public partial class GoogleAuthWindow : Window
    {
        private string _codeVerifier;
        private string _codeChallenge;
        private HttpListener? _listener;
        private TaskCompletionSource<string> _tcs;
        // клиентские данные из fierbase 
        private const string ClientId = "578615881260-3a2pum4qm94k30j72mo6hcehvv1pbrpf.apps.googleusercontent.com";
        private const string ClientSecret = "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";
        private const string RedirectUri = "http://localhost:8080/";
        
        private int _redirectPort;
        private bool _isAuthInProgress = false;

        public GoogleAuthWindow()
        {
            InitializeComponent();
            _codeVerifier = GenerateCodeVerifier();
            _codeChallenge = ComputeSha256Hash(_codeVerifier);
            _tcs = new TaskCompletionSource<string>();
            _redirectPort = GetAvailablePort();
            
            // Открываем браузер сразу после инициализации
            Loaded += async (s, e) => 
            {
                if (!_isAuthInProgress)
                {
                    _isAuthInProgress = true;
                    await StartOAuthFlowAsync();
                }
            };
        }
// запускает процесс авторизации
        private async Task StartOAuthFlowAsync()
        {
            try
            {
                StatusText.Text = "Запуск сервера...";
                
                // 1. Запускаем локальный сервер 
                await StartLocalServer();

                // 2. Открываем браузер для авторизации
                var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                              $"client_id={ClientId}" +
                              $"&redirect_uri={Uri.EscapeDataString($"http://localhost:{_redirectPort}")}" +
                              $"&response_type=code" +
                              $"&scope=openid%20email%20profile" +
                              $"&code_challenge={_codeChallenge}" +
                              $"&code_challenge_method=S256" +
                              $"&access_type=offline" +
                              $"&prompt=consent";

                StatusText.Text = "Открытие браузера...";
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });

                StatusText.Text = "Браузер открыт. Пожалуйста, авторизуйтесь...";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                _tcs.SetException(ex);
            }
        }
// запускает локальный сервер
        private async Task StartLocalServer()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_redirectPort}/");
                _listener.Start();

                // Запускаем в фоне без блокировки UI
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        var code = context.Request.QueryString["code"];
                        var error = context.Request.QueryString["error"];

                        Dispatcher.Invoke(() => 
                        {
                            StatusText.Text = "Обработка ответа...";
                        });

                        if (!string.IsNullOrEmpty(error))
                        {
                            var errorResponse = $"<html><body><h1>Ошибка авторизации: {error}</h1><p>Закройте это окно и попробуйте снова.</p></body></html>";
                            await SendResponse(context.Response, errorResponse);
                            _tcs.SetException(new Exception($"Ошибка авторизации: {error}"));
                            Dispatcher.Invoke(() => Close());
                            return;
                        }

                        if (!string.IsNullOrEmpty(code))
                        {
                            // Получаем токен по коду
                            Dispatcher.Invoke(() => 
                            {
                                StatusText.Text = "Получение токена...";
                            });

                            var idToken = await ExchangeCodeForTokenAsync(code);

                            if (string.IsNullOrEmpty(idToken))
                            {
                                var errorResponse = "<html><body><h1>Не удалось получить токен</h1><p>Попробуйте снова.</p></body></html>";
                                await SendResponse(context.Response, errorResponse);
                                _tcs.SetException(new Exception("Не удалось получить токен авторизации"));
                                Dispatcher.Invoke(() => Close());
                                return;
                            }

                            // Отправляем успешный ответ в браузер
                            var successResponse = "<html><body>" +
                                                "<h1>Авторизация успешна!</h1>" +
                                                "<p>Вы можете закрыть это окно и вернуться в приложение.</p>" +
                                                "<script>setTimeout(function() { window.close(); }, 2000);</script>" +
                                                "</body></html>";
                            await SendResponse(context.Response, successResponse);

                            _tcs.SetResult(idToken);
                            Dispatcher.Invoke(() => 
                            {
                                StatusText.Text = "Авторизация успешна!";
                                Close();
                            });
                        }
                        else
                        {
                            var noCodeResponse = "<html><body><h1>Не получен код авторизации</h1><p>Попробуйте снова.</p></body></html>";
                            await SendResponse(context.Response, noCodeResponse);
                            _tcs.SetException(new Exception("Не получен код авторизации"));
                            Dispatcher.Invoke(() => Close());
                        }
                    }
                    catch (Exception ex)
                    {
                        _tcs.SetException(ex);
                        Dispatcher.Invoke(() => 
                        {
                            StatusText.Text = $"Ошибка: {ex.Message}";
                        });
                    }
                    finally
                    {
                        _listener?.Stop();
                        _listener?.Close();
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска сервера: {ex.Message}");
                _tcs.SetException(ex);
                Close();
            }
        }
// отправляет HTML ответ в браузер после завершения авторизации
        private async Task SendResponse(HttpListenerResponse response, string content)
        {
            try
            {
                var buffer = Encoding.UTF8.GetBytes(content);
                response.ContentLength64 = buffer.Length;
                response.ContentType = "text/html; charset=utf-8";
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch { }
        }
// обменивает код авторизации на токены 
private async Task<string> ExchangeCodeForTokenAsync(string code)
{
    try
    {
        using var client = new HttpClient();
        var tokenUrl = "https://oauth2.googleapis.com/token";

        var requestBody = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["code_verifier"] = _codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = $"http://localhost:{_redirectPort}"
        };

        Console.WriteLine($"Отправка запроса токена с кодом: {code.Substring(0, Math.Min(10, code.Length))}...");
        Console.WriteLine($"Redirect URI: http://localhost:{_redirectPort}");
        
        var content = new FormUrlEncodedContent(requestBody);
        var response = await client.PostAsync(tokenUrl, content);
        var responseText = await response.Content.ReadAsStringAsync();
        
        Console.WriteLine($"Token response status: {response.StatusCode}");
        Console.WriteLine($"Token response body: {responseText}");

        if (!response.IsSuccessStatusCode)
        {
            // Попробуем парсить ошибку
            try
            {
                using var errorDoc = JsonDocument.Parse(responseText);
                if (errorDoc.RootElement.TryGetProperty("error", out var errorElement) &&
                    errorDoc.RootElement.TryGetProperty("error_description", out var descElement))
                {
                    throw new Exception($"Ошибка получения токена: {errorElement.GetString()} - {descElement.GetString()}");
                }
            }
            catch
            {
                throw new Exception($"Ошибка получения токена (HTTP {response.StatusCode}): {responseText}");
            }
        }

        using var doc = JsonDocument.Parse(responseText);
        
        string idToken = null;
        string accessToken = null;
        
        if (doc.RootElement.TryGetProperty("id_token", out var idTokenElement))
        {
            idToken = idTokenElement.GetString();
            Console.WriteLine($"Получен ID Token длиной: {idToken?.Length ?? 0}");
        }
        
        if (doc.RootElement.TryGetProperty("access_token", out var accessTokenElement))
        {
            accessToken = accessTokenElement.GetString();
            Console.WriteLine($"Получен Access Token длиной: {accessToken?.Length ?? 0}");
        }
        
        if (!string.IsNullOrEmpty(idToken))
        {
            Console.WriteLine($"Используем ID Token для Firebase");
            
            if (idToken.Length < 100)
            {
                throw new Exception($"Получен слишком короткий ID Token: {idToken.Length} символов");
            }
            
            return idToken;
        }

        throw new Exception("ID Token и Access Token не найдены в ответе. Получены поля: " + 
            string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name)));
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Ошибка обмена кода на токен: {ex.Message}\n\n" +
                       "Подробности в консоли.", "Ошибка", 
                       MessageBoxButton.OK, MessageBoxImage.Error);
        Console.WriteLine($"Полная ошибка ExchangeCodeForTokenAsync: {ex}");
        return null;
    }
}

// генерирует рандомный код верификации PKCE, преобразуя 32 байта в безопасную base64 строку 
        private string GenerateCodeVerifier()
        {
            var random = new Random();
            var bytes = new byte[32];
            random.NextBytes(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }
// преобразует SHA256-хэш в безопасную base64-строку 
        private string ComputeSha256Hash(string input)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }
        
// ищет свободный порт для локального сервера 
        private int GetAvailablePort()
        {
            try
            {
                // Пробуем порт 8080, если занят - находим свободный
                var listener = new TcpListener(IPAddress.Loopback, 8080);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
            catch
            {
                // Если порт 8080 занят, ищем любой свободный
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }
// возвращает объект задачи, который представляет асинхронную операцию получения idtoken 
        public Task<string> GetIdTokenAsync()
        {
            return _tcs.Task;
        }
//останавливает и закрывает локальный сервер 
        protected override void OnClosed(EventArgs e)
        {
            _listener?.Stop();
            _listener?.Close();
            base.OnClosed(e);
        }
// обработчик кнопки "отмена" 
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _tcs.SetCanceled();
            Close();
        }
//обработчик кнопки "открыть браузер снова"
        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isAuthInProgress)
                {
                    MessageBox.Show("Авторизация уже выполняется. Пожалуйста, подождите...", 
                        "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _isAuthInProgress = true;
                
                var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                              $"client_id={ClientId}" +
                              $"&redirect_uri={Uri.EscapeDataString($"http://localhost:{_redirectPort}")}" +
                              $"&response_type=code" +
                              $"&scope=openid%20email%20profile" +
                              $"&code_challenge={_codeChallenge}" +
                              $"&code_challenge_method=S256" +
                              $"&access_type=offline" +
                              $"&prompt=consent";

                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });
                
                StatusText.Text = "Браузер открыт. Пожалуйста, авторизуйтесь...";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                _isAuthInProgress = false;
            }
        }
        


    }
}