using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using System.Text.Json;

namespace VelsatMobile.Services.FirebaseService
{
    public interface IFirebaseService
    {
        Task<bool> SendPushNotificationAsync(string fcmToken, string titulo, string mensaje);
    }

    public class FirebaseService : IFirebaseService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<FirebaseService> _logger;

        public FirebaseService(IConfiguration configuration,
                               HttpClient httpClient,
                               ILogger<FirebaseService> logger)
        {
            _configuration = configuration;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<bool> SendPushNotificationAsync(string fcmToken, string titulo, string mensaje)
        {
            try
            {
                // 1. Obtener Bearer Token desde el JSON de Firebase
                string accessToken = await GetAccessTokenAsync();

                // 2. Construir el body
                var payload = new
                {
                    message = new
                    {
                        token = fcmToken,
                        notification = new
                        {
                            title = titulo,
                            body = mensaje
                        }
                    }
                };

                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // 3. Configurar header con Bearer Token
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

                // 4. Llamar a Firebase FCM
                string projectId = _configuration["Firebase:ProjectId"]; // "velsat-app"
                string url = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Firebase error: {error}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error enviando notificación Firebase: {ex.Message}");
                return false;
            }
        }

        private async Task<string> GetAccessTokenAsync()
        {
            // Credenciales de la cuenta de servicio guardadas como objeto en appsettings.json (Firebase:Credentials)
            var credentialsSection = _configuration.GetSection("Firebase:Credentials");
            if (!credentialsSection.Exists())
                throw new InvalidOperationException("Falta la sección Firebase:Credentials en la configuración");

            var credentialsJson = JsonSerializer.Serialize(
                credentialsSection.GetChildren().ToDictionary(c => c.Key, c => c.Value));

            GoogleCredential credential = CredentialFactory
                .FromJson<ServiceAccountCredential>(credentialsJson)
                .ToGoogleCredential()
                .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");

            return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        }
    }
}
