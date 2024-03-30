using System;
using System.Net.Sockets;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using isci.Allgemein;
using isci.Daten;
using isci.Beschreibung;
using Makaretu.Dns;

namespace isci.integration
{
    public class Konfiguration : Parameter
    {
        [fromEnv, fromArgs]
        public string[] Targets;

        [fromEnv, fromArgs]
        public string Adapter;

        [fromEnv, fromArgs]
        public int sendPort;

        [fromEnv, fromArgs]
        public int receivePort;

        [fromArgs, fromEnv]
        public int eingangPufferGroesseInBytes;

        [fromArgs, fromEnv]
        public int ausgangPufferGroesseInBytes;

        [fromArgs, fromEnv]
        public int warteZeit;

        public Konfiguration(string[] args) : base(args)
        {
            if (eingangPufferGroesseInBytes <= 1024) eingangPufferGroesseInBytes = 1024;
            if (ausgangPufferGroesseInBytes <= 1024) ausgangPufferGroesseInBytes = 1024;
        }
    }

    class Program
    {
        static Socket sendSocket;
        static Socket receiveSocket;
        static byte[] eingangPuffer;
        static readonly Dictionary<Dateneintrag, byte[]> eingegangeneAenderungen = [];

        static void UdpStart(IPAddress ip, int sendPort, int receivePort)
        {
            sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sendSocket.Bind(new IPEndPoint(ip, sendPort));

            receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            receiveSocket.Bind(new IPEndPoint(ip, receivePort));
            receiveSocket.BeginReceive(eingangPuffer, 0, eingangPuffer.Length, socketFlags: SocketFlags.None, UdpCallback, receiveSocket);
        }

        static void UdpCallback(IAsyncResult asyncResult){
            try
            {
                var recvSock = (Socket)asyncResult.AsyncState;
                //EndPoint client = new IPEndPoint(IPAddress.Any, 0);
                //int length = recvSock.EndReceiveFrom(asyncResult, ref client);
                var zahlEmpfangeneBytes = recvSock.EndReceive(asyncResult);

                //hier change lock machen?

                var eingangPufferPosition = 0;

                while(eingangPufferPosition < zahlEmpfangeneBytes)
                {
                    var adresse_ = new byte[8];
                    Array.Copy(eingangPuffer, eingangPufferPosition, adresse_, 0, adresse_.Length);
                    eingangPufferPosition += adresse_.Length;

                    var adresse_u = BitConverter.ToUInt64(adresse_, 0);
                    var eintrag_ = adressKartierung[adresse_u];
                    var type_ = datenstruktur[eintrag_].type;

                    int wertInBytesLaenge;

                    if (type_ == Datentypen.String || type_ == Datentypen.Objekt)
                    {
                        wertInBytesLaenge = BitConverter.ToUInt16(eingangPuffer, eingangPufferPosition);
                        eingangPufferPosition+=2;
                    } else {
                        wertInBytesLaenge = datentypGroesse[type_];
                    }

                    //Was mache ich wenn die Länge mit 0 determiniert wird?

                    var tmp_bytes = new byte[wertInBytesLaenge];
                    Array.Copy(eingangPuffer, eingangPufferPosition, tmp_bytes, 0, wertInBytesLaenge);

                    while(sperrFlagAenderungen) { }
                    try
                    {
                        eingegangeneAenderungen[datenstruktur[eintrag_]] = tmp_bytes;
                    } catch {

                    }
                    eingangPufferPosition+=wertInBytesLaenge;
                }
            }
            catch (Exception ex)
            {
                if (ex.GetBaseException().GetType() != typeof(KeyNotFoundException))
                {
                    Logger.Fehler("Ausnahme bei Datenempfang: die gesendete Variable ist auf diesem System nicht bekannt. Konfiguration der Datenmodelle überprüfen.");
                }
                else
                {
                    Logger.Fehler("Ausnahme bei Datenempfang: " + ex.Message + ", " + ex.StackTrace);
                }
            }
            finally
            {
                //eingangPuffer auf null setzen?
                receiveSocket.BeginReceive(eingangPuffer, 0, eingangPuffer.Length, socketFlags: SocketFlags.None, UdpCallback, receiveSocket); //muss ich den buffer wieder nullen?
            }
        }

        static readonly List<IPEndPoint> SendeZiele = [];
        static bool sperrFlagAenderungen;
        static Datenstruktur datenstruktur;

        static IPAddress HoleLokaleAdresse(string adapterName)
        {
            var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            var ips = Dns.GetHostAddresses(Dns.GetHostName());

            foreach (var adapter in adapters)
            {
                if (adapter.Name != adapterName) continue;
                var properties = adapter.GetIPProperties();
                foreach (var ip in properties.UnicastAddresses)
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        return ip.Address;
            }

            return new IPAddress(0);
        }

        static readonly Dictionary<UInt64, string> adressKartierung = [];
        static readonly Dictionary<string, UInt64> eintragKartierung = [];
        static readonly Dictionary<Datentypen, Int32> datentypGroesse = new ()
        {
            { Datentypen.Bool, sizeof(bool) },
            { Datentypen.UInt8, sizeof(byte) },
            { Datentypen.UInt16, sizeof(ushort) },
            { Datentypen.UInt32, sizeof(uint) },
            { Datentypen.UInt64, sizeof(ulong) },
            { Datentypen.Int8, sizeof(sbyte) },
            { Datentypen.Int16, sizeof(short) },
            { Datentypen.Int32, sizeof(int) },
            { Datentypen.Int64, sizeof(long) },
            { Datentypen.Float, sizeof(float) },
            { Datentypen.Double, sizeof(double) }
        };

        static void Main(string[] args)
        {
            var konfiguration = new Konfiguration(args);

            eingangPuffer = new byte[konfiguration.eingangPufferGroesseInBytes];

            /* var mdnsService = new ServiceProfile(konfiguration.Anwendung, konfiguration.Identifikation + ".isci", 1024); //Port 1024? Hat des einen Wert?
            mdnsService.AddProperty("Ressource", konfiguration.Ressource);
            mdnsService.AddProperty("Modul", "isci.integration");
            mdnsService.AddProperty("Port", konfiguration.Port.ToString());
            var mdnsDiscovery = new ServiceDiscovery();
            mdnsDiscovery.Advertise(mdnsService);
 */
            datenstruktur = new Datenstruktur(konfiguration);
            var ausfuehrungsmodell = new Ausführungsmodell(konfiguration, datenstruktur.Zustand);

            var beschreibung = new Modul(konfiguration.Identifikation, "isci.integration") {
                Name = "Integration Ressource " + konfiguration.Identifikation,
                Beschreibung = "Modul zur Integration"
            };
            beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");

            var ip = HoleLokaleAdresse(konfiguration.Adapter);
            while (ip.Equals(new IPAddress(0)))
            {
                System.Threading.Thread.Sleep(5000);
                ip = HoleLokaleAdresse(konfiguration.Adapter);
            }

            var schnittstelle = new SchnittstelleUdp(konfiguration.Ressource + "." + konfiguration.Anwendung + "." + konfiguration.Identifikation, ip.ToString(), konfiguration.sendPort, konfiguration.receivePort, konfiguration.Ressource);
            schnittstelle.Speichern(konfiguration.OrdnerSchnittstellen + "/" + schnittstelle.Identifikation + ".json");

            datenstruktur.DatenmodelleEinhängenAusOrdner(konfiguration.OrdnerDatenmodelle);

            foreach (var dateneintrag in datenstruktur.dateneinträge)
            {
                var id = dateneintrag.Value.Identifikation;
                var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id));
                var idAlsHashAdresse = BitConverter.ToUInt64(bytes);

                adressKartierung.Add(idAlsHashAdresse, id);
                eintragKartierung.Add(id, idAlsHashAdresse);
            }

            datenstruktur.Start();

            for (int i = 0; i < konfiguration.Targets.Length; ++i)
            {
                try
                {
                    var target_ = System.IO.File.ReadAllText(konfiguration.OrdnerSchnittstellen + "/" + konfiguration.Targets[i]);
                    var schnittstelleTarget = Newtonsoft.Json.JsonConvert.DeserializeObject<SchnittstelleUdp>(target_);
                    IPEndPoint endPoint = IPEndPoint.Parse(schnittstelleTarget.adresse);
                    endPoint.Port = schnittstelleTarget.receivePort;
                    SendeZiele.Add(endPoint);
                    //SendeZiele.Add(IPEndPoint.Parse(konfiguration.Targets[i]));
                }
                catch (Exception ex)
                {
                    Logger.Fehler("Ausnahme bei der Initialisierung der Sendeziele: " + ex.Message + ". " + konfiguration.OrdnerSchnittstellen + "/" + konfiguration.Targets[i] + " konnte nicht geladen werden.");
                }
            }

            UdpStart(ip, konfiguration.sendPort, konfiguration.receivePort);

            var ausgangPuffer = new byte[konfiguration.ausgangPufferGroesseInBytes];
            int ausgangPufferPosition = 0;            
            
            while(true)
            {
                datenstruktur.Zustand.WertAusSpeicherLesen();

                if (ausfuehrungsmodell.AktuellerZustandModulAktivieren())
                {
                    var zustandParameter = ausfuehrungsmodell.ParameterAktuellerZustand();
                    
                    switch (zustandParameter)
                    {
                        case "E":
                        {
                            sperrFlagAenderungen = true;
                            foreach (var aenderung in eingegangeneAenderungen)
                            {
                                try {
                                    aenderung.Key.WertAusBytes(aenderung.Value);
                                    aenderung.Key.WertInSpeicherSchreiben();
                                } catch {

                                }
                            }
                            eingegangeneAenderungen.Clear();
                            sperrFlagAenderungen = false;
                            break;
                        }
                        case "A":
                        {
                            var aktualisierteDateneintraege = datenstruktur.Lesen();
                            aktualisierteDateneintraege.RemoveAll(item => datenstruktur.nichtVerteilen.Contains(item));

                            //updated.Add("Debuganwendung.Test.beispiel");

                            foreach (var dateneintragId in aktualisierteDateneintraege)
                            {
                                var dateneintrag = datenstruktur[dateneintragId];

                                var idAlsHashAdresse = BitConverter.GetBytes(eintragKartierung[dateneintragId]);
                                byte[] dateneintragWertInBytes = null;

                                var dateneintragTyp = dateneintrag.type;

                                switch (dateneintragTyp)
                                {
                                    case Datentypen.String: 
                                    case Datentypen.Objekt: {
                                        var nachrichtInhalt = System.Text.Encoding.UTF8.GetBytes(dateneintrag.WertSerialisieren());
                                        var nachrichtLaenge = BitConverter.GetBytes(nachrichtInhalt.Length); //auf zwei Bytes normieren!!! maximale länge dann 65535
                                        var nachrichtLaengeNormiert = new byte[2];
                                        Array.Copy(nachrichtLaenge, nachrichtLaengeNormiert, nachrichtLaengeNormiert.Length);
                                        dateneintragWertInBytes = new byte[nachrichtInhalt.Length + nachrichtLaengeNormiert.Length];
                                        Array.Copy(nachrichtLaengeNormiert, dateneintragWertInBytes, length: nachrichtLaengeNormiert.Length);
                                        Array.Copy(nachrichtInhalt, 0, dateneintragWertInBytes, nachrichtLaengeNormiert.Length, nachrichtInhalt.Length);
                                        break;
                                    }
                                    case Datentypen.Bool: dateneintragWertInBytes = BitConverter.GetBytes((bool)dateneintrag.Wert); break;
                                    case Datentypen.Int8: dateneintragWertInBytes = [(byte)(sbyte)dateneintrag.Wert]; break;
                                    case Datentypen.Int16: dateneintragWertInBytes = BitConverter.GetBytes((Int16)dateneintrag.Wert); break;
                                    case Datentypen.Int32: dateneintragWertInBytes = BitConverter.GetBytes((Int32)dateneintrag.Wert); break;
                                    case Datentypen.Int64: dateneintragWertInBytes = BitConverter.GetBytes((Int64)dateneintrag.Wert); break;
                                    case Datentypen.UInt8: dateneintragWertInBytes = [(byte)dateneintrag.Wert]; break;
                                    case Datentypen.UInt16: dateneintragWertInBytes = BitConverter.GetBytes((UInt16)dateneintrag.Wert); break;
                                    case Datentypen.UInt32: dateneintragWertInBytes = BitConverter.GetBytes((UInt32)dateneintrag.Wert); break;
                                    case Datentypen.UInt64: dateneintragWertInBytes = BitConverter.GetBytes((UInt64)dateneintrag.Wert); break;
                                    case Datentypen.Float: dateneintragWertInBytes = BitConverter.GetBytes((float)dateneintrag.Wert); break;
                                    case Datentypen.Double: dateneintragWertInBytes = BitConverter.GetBytes((double)dateneintrag.Wert); break;
                                }

                                if (dateneintragWertInBytes != null)
                                {
                                    Array.Copy(idAlsHashAdresse, 0, ausgangPuffer, ausgangPufferPosition, idAlsHashAdresse.Length);
                                    ausgangPufferPosition += idAlsHashAdresse.Length;
                                    Array.Copy(dateneintragWertInBytes, 0, ausgangPuffer, ausgangPufferPosition, dateneintragWertInBytes.Length);
                                    ausgangPufferPosition += dateneintragWertInBytes.Length;
                                }
                            }

                            if (ausgangPufferPosition > 0)
                            {
                                foreach (var target in SendeZiele)
                                {
                                    //Console.WriteLine("Sende an " + target);
                                    sendSocket.BeginSendTo(ausgangPuffer, 0, ausgangPufferPosition, 0, target, null, null);
                                }
                                Array.Clear(ausgangPuffer, 0, ausgangPufferPosition);
                                ausgangPufferPosition = 0;
                            }
                            
                            break;
                        }
                    }

                    ausfuehrungsmodell.Folgezustand();
                    datenstruktur.Zustand.WertInSpeicherSchreiben();
                }

                isci.Helfer.SleepForMicroseconds(konfiguration.PauseArbeitsschleifeUs);
            }
        }
    }
}