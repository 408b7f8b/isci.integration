using System;
using System.Net.Sockets;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using isci.Allgemein;
using isci.Daten;
using isci.Beschreibung;

namespace isci.integration
{
    public class Konfiguration : Parameter
    {
        public string[] Targets;
        public string Adapter;
        public int Port;

        public Konfiguration(string datei) : base(datei) {

        }
    }

    class Program
    {
        static Socket udpSock;
        static byte[] buffer = new byte[1024];
        static Dictionary<string, string> aenderungen = new Dictionary<string, string>();

        static void udpStart(IPAddress ip, int port){
            udpSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSock.Bind(new IPEndPoint(ip, port));
            udpSock.BeginReceive(buffer, 0, buffer.Length, socketFlags:SocketFlags.None, udpCallback, udpSock);
        }       

        static void udpCallback(IAsyncResult asyncResult){
            try
            {
                Socket recvSock = (Socket)asyncResult.AsyncState;
                EndPoint client = new IPEndPoint(IPAddress.Any, 0);
                int length = recvSock.EndReceiveFrom(asyncResult, ref client);
                var conv_string = System.Text.UTF8Encoding.UTF8.GetString(buffer, 0, length);
                conv_string = conv_string.TrimEnd();

                var string_parts = conv_string.Split('#');
                while(change_lock) {}
                try
                {
                    if (aenderungen.ContainsKey(string_parts[0])) aenderungen[string_parts[0]] = string_parts[1];
                    else aenderungen.Add(string_parts[0], string_parts[1]);
                } catch {

                }

                udpSock.BeginReceive(buffer, 0, buffer.Length, socketFlags:SocketFlags.None, udpCallback, udpSock);
            } catch {
                
            }
        }

        static List<IPEndPoint> targets = new List<IPEndPoint>();
        static bool change_lock;
        static Datenstruktur structure;

        static IPAddress holeLokaleAdresse(string adapterName)
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

        static void Main(string[] args)
        {
            var konfiguration = new Konfiguration("konfiguration.json");
            
            structure = new Datenstruktur(konfiguration.OrdnerDatenstruktur);

            var dm = new Datenmodell(konfiguration.Identifikation);
            var cycle = new dtInt32(0, "cycle");
            dm.Dateneinträge.Add(cycle);

            var beschreibung = new Modul(konfiguration.Identifikation, "isci.integration", new ListeDateneintraege(){cycle});
            beschreibung.Name = "Integration Ressource " + konfiguration.Identifikation;
            beschreibung.Beschreibung = "Modul zur Integration";
            beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");

            dm.Speichern(konfiguration.OrdnerDatenmodelle + "/" + konfiguration.Identifikation + ".json");

            var ip = holeLokaleAdresse(konfiguration.Adapter);
            while (ip.Equals(new IPAddress(0)))
            {
                System.Threading.Thread.Sleep(5000);
                ip = holeLokaleAdresse(konfiguration.Adapter);
            }
            
            var schnittstelle = new SchnittstelleUdp(konfiguration.Ressource + ". " + konfiguration.Anwendung + "." + konfiguration.Identifikation, ip.ToString(), konfiguration.Ressource);
            schnittstelle.Speichern(konfiguration.OrdnerSchnittstellen + "/" + schnittstelle.Identifikation + ".json");

            structure.DatenmodellEinhängen(dm);
            structure.DatenmodelleEinhängenAusOrdner(konfiguration.OrdnerDatenmodelle);
            structure.Start();

            var Zustand = new dtInt32(0, "Zustand", konfiguration.OrdnerDatenstruktur + "/Zustand");
            Zustand.Start();

            for (int i = 0; i < konfiguration.Targets.Length; ++i)
            {
                try
                {
                    var target_ = System.IO.File.ReadAllText(konfiguration.OrdnerSchnittstellen + "/" + konfiguration.Targets[i]);
                    var schnittstelleTarget = Newtonsoft.Json.JsonConvert.DeserializeObject<SchnittstelleUdp>(target_);
                    targets.Add(IPEndPoint.Parse(schnittstelleTarget.adresse));
                } catch {

                }
            }

            udpStart(ip, konfiguration.Port);
            long curr_ticks = 0;
            
            while(true)
            {
                Zustand.Lesen();

                var erfüllteTransitionen = konfiguration.Ausführungstransitionen.Where(a => a.Eingangszustand == (System.Int32)Zustand.value);
                if (erfüllteTransitionen.Count<Ausführungstransition>() > 0)
                {
                    if ((System.Int32)Zustand.value == 0)
                    {
                        var curr_ticks_new = System.DateTime.Now.Ticks;
                        var ticks_span = curr_ticks_new - curr_ticks;
                        curr_ticks = curr_ticks_new;
                        cycle.value = (System.Int32)(ticks_span / System.TimeSpan.TicksPerMillisecond);
                        cycle.Schreiben();

                        change_lock = true;
                        foreach (var aenderung in aenderungen)
                        {
                            try {
                                structure.dateneinträge[aenderung.Key].AusString(aenderung.Value);
                                structure.dateneinträge[aenderung.Key].Schreiben();
                            } catch {

                            }                            
                        }
                        aenderungen.Clear();
                        change_lock = false;
                    } else {
                        var updated = structure.Lesen();

                        foreach (var entry in updated)
                        {
                            if (structure.dateneinträge[entry].Identifikation == "ns=integration;cycle") continue;
                            var snd = System.Text.UTF8Encoding.UTF8.GetBytes(entry + "#" + structure.dateneinträge[entry].Serialisieren());
                            foreach (var target in targets) {
                                udpSock.BeginSendTo(snd, 0, snd.Length, 0, target, null, null);
                            }
                            
                            structure.dateneinträge[entry].aenderung = false;
                        }
                    }

                    Zustand.value = erfüllteTransitionen.First<Ausführungstransition>().Ausgangszustand;
                    Zustand.Schreiben();
                }
            }
        }
    }
}