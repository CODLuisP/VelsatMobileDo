using Dapper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using VelsatBackendAPI.Model;
using VelsatMobile.Model;
using VelsatMobile.Model.NotificacionesMobile;

namespace VelsatMobile.Data.Repositories
{
    public class AplicativoRepository : IAplicativoRepository
    {
        private readonly IDbConnection _defaultConnection; IDbTransaction _defaultTransaction;

        public AplicativoRepository(IDbConnection defaultconnection, IDbTransaction defaulttransaction)
        {
            _defaultConnection = defaultconnection;
            _defaultTransaction = defaulttransaction;
        }

        public async Task<IEnumerable<Unidad>> GetUnidadesUsuario(string accountID)
        {
            string sql = @"
        SELECT DISTINCT deviceID 
        FROM (
            SELECT deviceID FROM device WHERE accountID = @AccountID 
            UNION ALL 
            SELECT deviceID FROM urbano.deviceuser WHERE userID = @AccountID AND Status = 1
        ) AS combined_devices";

            return await _defaultConnection.QueryAsync<Unidad>(sql, new { AccountID = accountID });
        }

        //Método para obtener notificaciones de todo un usuario
        public async Task<IEnumerable<Notificaciones>> GetNotifications(string accountID)
        {
            string sql = @"Select accountID, deviceID, timestamp, statusCode, latitude, longitude, speedKPH from eventdata where accountID = @AccountID and timestamp >= UNIX_TIMESTAMP(CURDATE()) and statusCode IN (61714, 61715) order by timestamp DESC";

            return await _defaultConnection.QueryAsync<Notificaciones>(sql, new { AccountID = accountID });
        }

        //Método para obtener los kilómetros recorridos en un día
        public async Task<double> GetKilometersDay(string deviceID)
        {
            string sql = @"SELECT (MAX(odometerKM) - MIN(odometerKM)) as km FROM eventdata WHERE deviceID = @DeviceID AND timestamp >= UNIX_TIMESTAMP(CURDATE())";

            var result = await _defaultConnection.QueryFirstOrDefaultAsync<double?>(sql, new { DeviceID = deviceID });

            return result ?? 0;
        }

        //Método para obtener notificaciones de una unidad
        public async Task<IEnumerable<Notificaciones>> GetNotifDevice(string accountID, string deviceID)
        {
            string sql = @"Select accountID, deviceID, timestamp, statusCode, latitude, longitude, speedKPH from eventdata where accountID = @AccountID and deviceID = @DeviceID and timestamp >= UNIX_TIMESTAMP(CURDATE()) and statusCode IN (61714, 61715) order by timestamp DESC";

            return await _defaultConnection.QueryAsync<Notificaciones>(sql, new { AccountID = accountID, DeviceID = deviceID });
        }

        //NOTIFICACIONES
        public async Task<List<Documento>> DocumentosPorVencer(string accountID)
        {
            var fechaActual = DateTime.UtcNow.AddHours(-5).Date;
            var fechaLimite = fechaActual.AddDays(30);

            const string sql = @"SELECT * FROM documentos WHERE Fecha_vencimiento IS NOT NULL AND Fecha_vencimiento <= @FechaLimite AND accountID = @AccountID ORDER BY Fecha_vencimiento ASC";
            try
            {
                var documentos = await _defaultConnection.QueryAsync<Documento>(sql, new
                {
                    FechaLimite = fechaLimite,
                    AccountID = accountID
                }, transaction: _defaultTransaction);
                return documentos.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al obtener documentos próximos a vencer");
                return new List<Documento>();
            }
        }

        public async Task<bool> SaveFCMTokenAsync(string userId, string fcmToken, string platform)
        {
            // Verificar si ya existe exactamente ese userId + token
            string sqlCheck = @"SELECT COUNT(1) FROM UserFCMTokens 
                        WHERE UserId = @UserId AND FCMToken = @FCMToken";

            int exists = await _defaultConnection.ExecuteScalarAsync<int>(
                sqlCheck,
                new { UserId = userId, FCMToken = fcmToken },
                transaction: _defaultTransaction
            );

            if (exists > 0)
                return true; // Ya existe, no hacer nada

            // No existe → INSERT (nuevo dispositivo o token cambió)
            string sqlInsert = @"INSERT INTO UserFCMTokens 
                         (UserId, FCMToken, Platform, CreatedAt, UpdatedAt)
                         VALUES (@UserId, @FCMToken, @Platform, NOW(), NOW())";

            int rows = await _defaultConnection.ExecuteAsync(
                sqlInsert,
                new { UserId = userId, FCMToken = fcmToken, Platform = platform },
                transaction: _defaultTransaction
            );

            return rows > 0;
        }

        // Necesitas este método para obtener todos los usuarios con token
        public async Task<IEnumerable<UserFCMToken>> GetAllUserFCMTokensAsync()
        {
            string sql = @"SELECT Id, userId, fcmtoken, platform, createdAt, updatedAt FROM userfcmtokens";

            return await _defaultConnection.QueryAsync<UserFCMToken>(
                sql,
                transaction: _defaultTransaction
            );
        }

        public async Task<int> ActualizarDeviceServicio(string deviceID, string codservicio)
        {
            string sql = @"UPDATE device SET servicio = @Codservicio WHERE deviceID = @DeviceID";

            return await _defaultConnection.ExecuteAsync(sql,
                new { Codservicio = codservicio, DeviceID = deviceID },
                transaction: _defaultTransaction);
        }

        public async Task<int> ActualizarDeviceFinServicio(string deviceID)
        {
            string sql = @"UPDATE device SET servicio = NULL WHERE deviceID = @DeviceID";

            return await _defaultConnection.ExecuteAsync(sql,
                new { DeviceID = deviceID },
                transaction: _defaultTransaction);
        }
    }
}
