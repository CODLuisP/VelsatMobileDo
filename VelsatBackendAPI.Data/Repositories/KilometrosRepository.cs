using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using VelsatBackendAPI.Model;

namespace VelsatBackendAPI.Data.Repositories
{
    public class KilometrosRepository : IKilometrosRepository
    {
        private readonly IDbConnection _defaultConnection;
        private readonly IDbConnection _secondConnection;
        private readonly IDbTransaction _defaultTransaction;
        private readonly IDbTransaction _secondTransaction;

        public KilometrosRepository(IDbConnection defaultConnection, IDbConnection secondConnection, IDbTransaction defaulttransaction, IDbTransaction secondTransaction)
        {
            _defaultConnection = defaultConnection;
            _secondConnection = secondConnection;
            _defaultTransaction = defaulttransaction;
            _secondTransaction = secondTransaction;

        }

        public async Task<KilometrosReporting> GetKmReporting(string fechaini, string fechafin, string deviceID, string accountID)
        {
            var dates = FormatDate(fechaini, fechafin);

            fechaini = dates.dateStart;
            fechafin = dates.dateEnd;

            var resultadoDias = CalcularDias(fechaini, fechafin);

            double numdias = resultadoDias.NumDias;

            if (numdias <= 5)
            {
                const string sql = "select tabla from historicos where timeini<=@FechafinUnix and timefin>=@FechainiUnix";

                var nombresTablas = _defaultConnection.Query<Historicos>(sql, new { FechainiUnix = resultadoDias.UnixFechaInicio, FechafinUnix = resultadoDias.UnixFechaFin }, transaction: _defaultTransaction).ToList();

                var kilometrosReporting = new KilometrosReporting
                {
                    ListaKilometros = new List<KilometrosRecorridos>()
                };

                if (nombresTablas.Count == 0)
                {
                    // Si no se encontraron nombres de tablas, consultamos directamente la tabla "eventdata"
                    string sqlEventData = @"select deviceID, MAX(odometerKM) AS maximo, MIN(odometerKM) AS minimo, (MAX(odometerKM) - MIN(odometerKM)) as kilometros from eventdata where accountID = @AccountID and deviceID = @DeviceID and timestamp between @FechainiUnix and @FechafinUnix group by deviceID";

                    kilometrosReporting.ListaKilometros = _defaultConnection.Query<KilometrosRecorridos>(sqlEventData, new { AccountID = accountID, DeviceID = deviceID, FechainiUnix = resultadoDias.UnixFechaInicio, FechafinUnix = resultadoDias.UnixFechaFin }, transaction: _defaultTransaction).ToList();

                    for (int i = 0; i < kilometrosReporting.ListaKilometros.Count; i++)
                    {
                        kilometrosReporting.ListaKilometros[i].Item = i + 1;
                    }

                }

                else

                {
                    foreach (var nombreTabla in nombresTablas)
                    {
                        string consultaTabla = nombreTabla.Tabla;

                        string sqlR = $@"select deviceID, MAX(odometerKM) AS maximo, MIN(odometerKM) AS minimo, (MAX(odometerKM) - MIN(odometerKM)) as kilometros from {consultaTabla} where accountID = @AccountID and deviceID = @DeviceID and timestamp between @FechainiUnix and @FechafinUnix group by deviceID";

                        var datosTabla = _secondConnection.Query<KilometrosRecorridos>(sqlR, new { AccountID = accountID, DeviceID = deviceID, FechainiUnix = resultadoDias.UnixFechaInicio, FechafinUnix = resultadoDias.UnixFechaFin }, transaction: _secondTransaction).ToList();

                        kilometrosReporting.ListaKilometros.AddRange(datosTabla);

                    }

                    for (int i = 0; i < kilometrosReporting.ListaKilometros.Count; i++)
                    {
                        kilometrosReporting.ListaKilometros[i].Item = i + 1;
                    }
                }

                if (kilometrosReporting.ListaKilometros.Count == 0 || kilometrosReporting.ListaKilometros == null)
                {
                    return new KilometrosReporting
                    {
                        Mensaje = "No se encontro datos disponible en el rango de fechas ingresado"
                    };
                }

                return kilometrosReporting;
            }
            else
            {
                return new KilometrosReporting
                {
                    Mensaje = "La diferencia entre las fechas es mayor a 5 días; seleccione otra fechas"
                };
            }
        }

        public async Task<KilometrosReporting> GetAllKmReporting(string fechaini, string fechafin, string accountID)
        {
            var dates = FormatDate(fechaini, fechafin);
            fechaini = dates.dateStart;
            fechafin = dates.dateEnd;

            var resultadoDias = CalcularDias(fechaini, fechafin);
            double numdias = resultadoDias.NumDias;

            var kilometrosReporting = new KilometrosReporting
            {
                ListaKilometros = new List<KilometrosRecorridos>()
            };

            if (numdias <= 5)
            {
                // Consultar la tabla "historicos"
                const string sql = "select tabla from historicos where timeini <= @FechafinUnix and timefin >= @FechainiUnix";
                var nombresTablas = _defaultConnection.Query<Historicos>(sql, new { FechainiUnix = resultadoDias.UnixFechaInicio, FechafinUnix = resultadoDias.UnixFechaFin }, transaction: _defaultTransaction).ToList();

                if (nombresTablas.Count == 0)
                {
                    // Consultar directamente la tabla "eventdata"
                    string sqlEventData = $@"select deviceID, MAX(odometerKM) AS maximo, MIN(odometerKM) AS minimo, (MAX(odometerKM) - MIN(odometerKM)) as kilometros from eventdata where deviceID in (select deviceID from device where accountID=@AccountID) and timestamp between @FechainiUnix and @FechafinUnix group by deviceID";


                    var parameters = new DynamicParameters();
                    parameters.Add("FechainiUnix", resultadoDias.UnixFechaInicio);
                    parameters.Add("FechafinUnix", resultadoDias.UnixFechaFin);
                    parameters.Add("AccountID", accountID);

                    kilometrosReporting.ListaKilometros = _defaultConnection.Query<KilometrosRecorridos>(sqlEventData, parameters, transaction: _defaultTransaction).ToList();
                }
                else
                {
                    foreach (var nombreTabla in nombresTablas)
                    {
                        string consultaTabla = nombreTabla.Tabla;

                        // Consultar cada tabla histórica
                        string sqlR = $@"select deviceID, MAX(odometerKM) AS maximo, MIN(odometerKM) AS minimo, (MAX(odometerKM) - MIN(odometerKM)) as kilometros from {consultaTabla} where deviceID in (select deviceID from device where accountID=@AccountID) and timestamp between @FechainiUnix and @FechafinUnix group by deviceID";

                        var parameters = new DynamicParameters();
                        parameters.Add("FechainiUnix", resultadoDias.UnixFechaInicio);
                        parameters.Add("FechafinUnix", resultadoDias.UnixFechaFin);
                        parameters.Add("AccountID", accountID);

                        var datosTabla = _secondConnection.Query<KilometrosRecorridos>(sqlR, parameters, transaction: _secondTransaction).ToList();
                        kilometrosReporting.ListaKilometros.AddRange(datosTabla);
                    }
                }

                for (int i = 0; i < kilometrosReporting.ListaKilometros.Count; i++)
                {
                    kilometrosReporting.ListaKilometros[i].Item = i + 1;
                }

                if (kilometrosReporting.ListaKilometros.Count == 0)
                {
                    return new KilometrosReporting
                    {
                        Mensaje = "No se encontró datos disponible en el rango de fechas ingresado"
                    };
                }

                return kilometrosReporting;
            }
            else
            {
                return new KilometrosReporting
                {
                    Mensaje = "La diferencia entre las fechas es mayor a 5 días; seleccione otra fechas"
                };
            }
        }

        public class ResultadosCalculoDias
        {
            public double NumDias { get; set; }
            public int UnixFechaInicio { get; set; }
            public int UnixFechaFin { get; set; }
        }

        public int DateUnix(string fecha)
        {
            fecha = WebUtility.UrlDecode(fecha);

            DateTime fechaTime = DateTime.ParseExact(fecha, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

            int unixFecha = (int)(fechaTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds;

            return unixFecha;
        }

        public ResultadosCalculoDias CalcularDias(string fechaI, string fechaF)
        {
            int fechainiUnix = DateUnix(fechaI);
            int fechafinUnix = DateUnix(fechaF);

            int totalsegundos = fechafinUnix - fechainiUnix;
            double numdias = (double)totalsegundos / 86400;

            return new ResultadosCalculoDias
            {
                NumDias = numdias,
                UnixFechaInicio = fechainiUnix,
                UnixFechaFin = fechafinUnix,
            };
        }

        public class ResultDate
        {
            public string dateStart { get; set; }
            public string dateEnd { get; set; }

        }

        public ResultDate FormatDate(string dateS, string dateE)
        {

            DateTime.TryParse(dateS, out DateTime fechaInicio);
            DateTime.TryParse(dateE, out DateTime fechaFin);


            string fechaInicioString = fechaInicio.ToString("dd/MM/yyyy HH:mm");
            string fechaFinString = fechaFin.ToString("dd/MM/yyyy HH:mm");

            return new ResultDate
            {
                dateStart = fechaInicioString,
                dateEnd = fechaFinString,
            };
        }
    }
}
