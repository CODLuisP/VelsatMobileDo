using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using VelsatBackendAPI.Model;
using VelsatMobile.Model;

namespace VelsatBackendAPI.Data.Services
{
    public class DatosCargainicialService : IDatosCargainicialService
    {
        private readonly IDbConnection _defaultConnection;
        private readonly IDbTransaction _defaultTransaction;
        private List<string> _deviceIds;
        private string _currentLogin;

        public DatosCargainicialService(IDbConnection defaultConnection, IDbTransaction defaultTransaction)
        {
            _defaultConnection = defaultConnection;
            _defaultTransaction = defaultTransaction;
        }

        private async Task<List<string>> GetDeviceIdsAsync(string login)
        {
            if (_deviceIds != null && _currentLogin == login)
            {
                return _deviceIds;
            }

            const string sqlGetAllDeviceIds = @"
                SELECT DISTINCT deviceID 
                FROM (
                    SELECT deviceID FROM device WHERE accountID = @Login 
                    UNION ALL 
                    SELECT deviceID FROM urbano.deviceuser WHERE userID = @Login AND Status = 1
                ) AS combined_devices";

            _deviceIds = (await _defaultConnection.QueryAsync<string>(
                sqlGetAllDeviceIds,
                new { Login = login },
                transaction: _defaultTransaction)).ToList();

            _currentLogin = login;

            return _deviceIds;
        }

        public async Task<DatosCargainicial> ObtenerDatosCargaInicialAsync(string login)
        {
            var deviceIds = await GetDeviceIdsAsync(login);

            if (!deviceIds.Any())
            {
                return new DatosCargainicial
                {
                    FechaActual = DateTime.Now,
                    DatosDevice = new List<Device>()
                };
            }

            const string sqlGetDevices = @"
                SELECT deviceID, lastGPSTimestamp, lastValidLatitude, lastValidLongitude, 
                       lastOdometerKM, direccion, lastValidHeading, lastValidSpeed 
                FROM device 
                WHERE deviceID IN @DeviceIDs";

            var devices = (await _defaultConnection.QueryAsync<Device>(
                sqlGetDevices,
                new { DeviceIDs = deviceIds },
                transaction: _defaultTransaction)).ToList();

            return new DatosCargainicial
            {
                FechaActual = DateTime.Now,
                DatosDevice = devices
            };
        }

        public async Task<IEnumerable<SimplifiedDevice>> SimplifiedList(string login)
        {
            var deviceIds = await GetDeviceIdsAsync(login);

            if (!deviceIds.Any())
            {
                return new List<SimplifiedDevice>();
            }

            const string sqlGetSimplifiedDevices = @"
                SELECT DISTINCT d.deviceID, d.lastValidLatitude, d.lastValidLongitude, 
                       d.lastValidSpeed, d.direccion 
                FROM device d 
                WHERE d.deviceID IN @DeviceIds";

            var listDevices = await _defaultConnection.QueryAsync<SimplifiedDevice>(
                sqlGetSimplifiedDevices,
                new { DeviceIds = deviceIds },
                transaction: _defaultTransaction);

            return listDevices;
        }

        public async Task<DatosVehiculo> ObtenerDatosVehiculoAsync(string login, string placa)
        {
            var deviceIds = await GetDeviceIdsAsync(login);

            if (!deviceIds.Any())
            {
                return new DatosVehiculo
                {
                    FechaActual = DateTime.Now,
                    Vehiculo = null
                };
            }

            const string sqlGetVehicle = @"
                SELECT deviceID, lastGPSTimestamp, lastValidLatitude, lastValidLongitude, 
                       lastValidHeading, lastValidSpeed, lastOdometerKM, direccion 
                FROM device 
                WHERE deviceID IN @DeviceIDs AND deviceID = @Placa";

            var vehiculo = await _defaultConnection.QueryFirstOrDefaultAsync<Device>(
                sqlGetVehicle,
                new { DeviceIDs = deviceIds, Placa = placa },
                transaction: _defaultTransaction);

            return new DatosVehiculo
            {
                FechaActual = DateTime.Now,
                Vehiculo = vehiculo
            };
        }

        public void ClearDeviceIdsCache()
        {
            _deviceIds = null;
            _currentLogin = null;
        }
    }
}