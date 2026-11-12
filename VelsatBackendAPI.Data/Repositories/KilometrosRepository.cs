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
                // La depuración diaria mueve a historicos, con un retraso de 7 días, el registro
                // del día calendario correspondiente (hoy - 7). Lo posterior sigue en eventdata.
                int unixLimite = GetUnixLimiteDepuracion();

                List<KilometrosRecorridos> lista;

                if (resultadoDias.UnixFechaFin <= unixLimite)
                {
                    lista = ConsultarHistoricosKm(accountID, deviceID, resultadoDias.UnixFechaInicio, resultadoDias.UnixFechaFin);
                }
                else if (resultadoDias.UnixFechaInicio > unixLimite)
                {
                    lista = ConsultarEventDataKm(accountID, deviceID, resultadoDias.UnixFechaInicio, resultadoDias.UnixFechaFin);
                }
                else
                {
                    var datosHistoricos = ConsultarHistoricosKm(accountID, deviceID, resultadoDias.UnixFechaInicio, unixLimite);
                    var datosEventData = ConsultarEventDataKm(accountID, deviceID, unixLimite + 1, resultadoDias.UnixFechaFin);

                    lista = CombinarKilometros(datosHistoricos, datosEventData);
                }

                var kilometrosReporting = new KilometrosReporting
                {
                    ListaKilometros = lista
                };

                for (int i = 0; i < kilometrosReporting.ListaKilometros.Count; i++)
                {
                    kilometrosReporting.ListaKilometros[i].Item = i + 1;
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
                int unixLimite = GetUnixLimiteDepuracion();

                if (resultadoDias.UnixFechaFin <= unixLimite)
                {
                    kilometrosReporting.ListaKilometros = ConsultarHistoricosKmAll(accountID, resultadoDias.UnixFechaInicio, resultadoDias.UnixFechaFin);
                }
                else if (resultadoDias.UnixFechaInicio > unixLimite)
                {
                    kilometrosReporting.ListaKilometros = ConsultarEventDataKmAll(accountID, resultadoDias.UnixFechaInicio, resultadoDias.UnixFechaFin);
                }
                else
                {
                    var datosHistoricos = ConsultarHistoricosKmAll(accountID, resultadoDias.UnixFechaInicio, unixLimite);
                    var datosEventData = ConsultarEventDataKmAll(accountID, unixLimite + 1, resultadoDias.UnixFechaFin);

                    kilometrosReporting.ListaKilometros = CombinarKilometros(datosHistoricos, datosEventData);
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

        private int GetUnixLimiteDepuracion()
        {
            // La depuración diaria mueve a historicos, con un retraso de 7 días, el registro
            // del día calendario correspondiente (hoy - 7). Todo lo posterior sigue en eventdata.
            DateTime diaLimite = DateTime.Now.Date.AddDays(-7);
            return DateUnix(diaLimite.ToString("dd/MM/yyyy") + " 23:59");
        }

        private List<KilometrosRecorridos> ConsultarHistoricosKm(string accountID, string deviceID, int unixFechaInicio, int unixFechaFin)
        {
            const string sql = "select tabla from historicos where timeini<=@FechafinUnix and timefin>=@FechainiUnix";

            var nombresTablas = _defaultConnection.Query<Historicos>(sql, new { FechainiUnix = unixFechaInicio, FechafinUnix = unixFechaFin }, transaction: _defaultTransaction).ToList();

            if (nombresTablas.Count == 0)
            {
                return ConsultarEventDataKm(accountID, deviceID, unixFechaInicio, unixFechaFin);
            }

            var lista = new List<KilometrosRecorridos>();

            foreach (var nombreTabla in nombresTablas)
            {
                string consultaTabla = nombreTabla.Tabla;

                string sqlR = $@"select deviceID, MAX(odometerKM) AS maximo, MIN(odometerKM) AS minimo, (MAX(odometerKM) - MIN(odometerKM)) as kilometros from {consultaTabla} where accountID = @AccountID and deviceID = @DeviceID and timestamp between @FechainiUnix and @FechafinUnix group by deviceID";

                var datosTabla = _secondConnection.Query<KilometrosRecorridos>(sqlR, new { AccountID = accountID, DeviceID = deviceID, FechainiUnix = unixFechaInicio, FechafinUnix = unixFechaFin }, transaction: _secondTransaction).ToList();

                lista.AddRange(datosTabla);
            }

            return lista;
        }

        private List<KilometrosRecorridos> ConsultarEventDataKm(string accountID, string deviceID, int unixFechaInicio, int unixFechaFin)
        {
            const string sqlEventData = @"select deviceID, MAX(odometerKM) AS maximo, MIN(odometerKM) AS minimo, (MAX(odometerKM) - MIN(odometerKM)) as kilometros from eventdata where accountID = @AccountID and deviceID = @DeviceID and timestamp between @FechainiUnix and @FechafinUnix group by deviceID";

            return _defaultConnection.Query<KilometrosRecorridos>(sqlEventData, new { AccountID = accountID, DeviceID = deviceID, FechainiUnix = unixFechaInicio, FechafinUnix = unixFechaFin }, transaction: _defaultTransaction).ToList();
        }

        private List<KilometrosRecorridos> ConsultarHistoricosKmAll(string accountID, int unixFechaInicio, int unixFechaFin)
        {
            const string sql = "select tabla from historicos where timeini <= @FechafinUnix and timefin >= @FechainiUnix";
            var nombresTablas = _defaultConnection.Query<Historicos>(sql, new { FechainiUnix = unixFechaInicio, FechafinUnix = unixFechaFin }, transaction: _defaultTransaction).ToList();

            if (nombresTablas.Count == 0)
            {
                return ConsultarEventDataKmAll(accountID, unixFechaInicio, unixFechaFin);
            }

            var lista = new List<KilometrosRecorridos>();

            foreach (var nombreTabla in nombresTablas)
            {
                string consultaTabla = nombreTabla.Tabla;

                string sqlR = $@"select deviceID, MAX(odometerKM) AS maximo, MIN(odometerKM) AS minimo, (MAX(odometerKM) - MIN(odometerKM)) as kilometros from {consultaTabla} where deviceID in (select deviceID from device where accountID=@AccountID) and timestamp between @FechainiUnix and @FechafinUnix group by deviceID";

                var parameters = new DynamicParameters();
                parameters.Add("FechainiUnix", unixFechaInicio);
                parameters.Add("FechafinUnix", unixFechaFin);
                parameters.Add("AccountID", accountID);

                var datosTabla = _secondConnection.Query<KilometrosRecorridos>(sqlR, parameters, transaction: _secondTransaction).ToList();
                lista.AddRange(datosTabla);
            }

            return lista;
        }

        private List<KilometrosRecorridos> ConsultarEventDataKmAll(string accountID, int unixFechaInicio, int unixFechaFin)
        {
            const string sqlEventData = @"select deviceID, MAX(odometerKM) AS maximo, MIN(odometerKM) AS minimo, (MAX(odometerKM) - MIN(odometerKM)) as kilometros from eventdata where deviceID in (select deviceID from device where accountID=@AccountID) and timestamp between @FechainiUnix and @FechafinUnix group by deviceID";

            var parameters = new DynamicParameters();
            parameters.Add("FechainiUnix", unixFechaInicio);
            parameters.Add("FechafinUnix", unixFechaFin);
            parameters.Add("AccountID", accountID);

            return _defaultConnection.Query<KilometrosRecorridos>(sqlEventData, parameters, transaction: _defaultTransaction).ToList();
        }

        // Si el rango cruza el corte, el máximo y mínimo de odómetro se combinan por dispositivo.
        private List<KilometrosRecorridos> CombinarKilometros(List<KilometrosRecorridos> a, List<KilometrosRecorridos> b)
        {
            var combinado = new List<KilometrosRecorridos>();

            foreach (var grupo in a.Concat(b).GroupBy(k => k.DeviceId))
            {
                double maximo = grupo.Max(k => k.Maximo);
                double minimo = grupo.Min(k => k.Minimo);

                combinado.Add(new KilometrosRecorridos
                {
                    DeviceId = grupo.Key,
                    Maximo = maximo,
                    Minimo = minimo,
                    Kilometros = maximo - minimo
                });
            }

            return combinado;
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
