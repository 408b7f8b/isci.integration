﻿using System;
using System.Net.Sockets;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using isci.Allgemein;
using isci.Daten;
using isci.Beschreibung;
using Makaretu.Dns;
using System.Reflection.Metadata;

namespace isci.integration
{
    public class Konfiguration : isci.Allgemein.Parameter
    {
        public string[] Targets;
        public string Adapter;
        public int Port;

        public Konfiguration(string[] args) : base(args)
        {
            
        }
    }

    class Program
    {
        static Socket udpSock;
        static byte[] buffer;
        static Dictionary<Dateneintrag, byte[]> aenderungen = new Dictionary<Dateneintrag, byte[]>();

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

                //buffer für weiterverarbeitung zwischenspeichern, und gleich wieder in beginreceivefrom, um nichts zu verpassen?

                //hier change lock machen?

                if (enc != null)
                {
                    var tmp = enc.Decrypt(buffer);
                    Array.Copy(tmp, buffer, tmp.Length);
                    length = tmp.Length;
                }

                var pos_ = 0;

                while(pos_ < length)
                {
                    var adresse_ = new byte[8];
                    Array.Copy(buffer, pos_, adresse_, 0, adresse_.Length);
                    pos_ += adresse_.Length;

                    var adresse_u = BitConverter.ToUInt64(adresse_, 0);
                    var eintrag_ = adressKartierung[adresse_u];
                    var type_ = eintrag_.type;

                    int laenge;

                    if (type_ == Datentypen.String || type_ == Datentypen.Objekt)
                    {
                        laenge = BitConverter.ToUInt16(buffer, pos_);
                        pos_+=2;
                    } else {
                        laenge = datentypGroesse[type_];
                    }

                    var tmp_bytes = new byte[laenge];
                    Array.Copy(buffer, pos_, tmp_bytes, 0, laenge);

                    while(change_lock) {}
                    try
                    {
                        aenderungen[eintrag_] = tmp_bytes;
                    } catch {

                    }
                    pos_+=laenge;
                }

                udpSock.BeginReceive(buffer, 0, buffer.Length, socketFlags:SocketFlags.None, udpCallback, udpSock); //muss ich den buffer wieder nullen?
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

        static Dictionary<UInt64, Dateneintrag> adressKartierung = new Dictionary<UInt64, Dateneintrag>();
        static Dictionary<Dateneintrag, UInt64> eintragKartierung = new Dictionary<Dateneintrag, UInt64>();
        static readonly Dictionary<Datentypen, Int32> datentypGroesse = new Dictionary<Datentypen, Int32>()
        {
            { Datentypen.Bool, sizeof(bool) },
            { Datentypen.UInt8, sizeof(byte) },
            { Datentypen.UInt16, sizeof(ushort) },
            { Datentypen.UInt32, sizeof(uint) },
            { Datentypen.Int8, sizeof(sbyte) },
            { Datentypen.Int16, sizeof(short) },
            { Datentypen.Int32, sizeof(int) },
            { Datentypen.Float, sizeof(float) },
            { Datentypen.Double, sizeof(double) }
        };

        static isci.Daten.UdpPacketEncryption enc = null;

        static void Main(string[] args)
        {
            var konfiguration = new Konfiguration(args);

            if (System.IO.File.Exists(konfiguration.OrdnerBeschreibungen + "/anwendungsschluessel"))
            {
                try {
                    enc = new isci.Daten.UdpPacketEncryption(konfiguration.OrdnerBeschreibungen + "/anwendungsschluessel");
                } catch {

                }                
            }

            if (enc == null)
            {
                buffer = new byte[1024];
            } else {
                buffer = new byte[1300]; //genaue länge noch verifizieren? kleinstmöglich --> performancevorteil?
            }

            var service = new ServiceProfile(konfiguration.Anwendung, konfiguration.Identifikation + ".isci", 1024);
            service.AddProperty("Ressource", konfiguration.Ressource);
            service.AddProperty("Modul", "isci.integration");
            service.AddProperty("Port", konfiguration.Port.ToString());
            var sd = new ServiceDiscovery();
            sd.Advertise(service);

            structure = new Datenstruktur(konfiguration);
            var ausfuehrungsmodell = new Ausführungsmodell(konfiguration, structure.Zustand);

            var beschreibung = new Modul(konfiguration.Identifikation, "isci.integration");
            beschreibung.Name = "Integration Ressource " + konfiguration.Identifikation;
            beschreibung.Beschreibung = "Modul zur Integration";
            beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");

            var ip = holeLokaleAdresse(konfiguration.Adapter);
            while (ip.Equals(new IPAddress(0)))
            {
                System.Threading.Thread.Sleep(5000);
                ip = holeLokaleAdresse(konfiguration.Adapter);
            }
            
            var schnittstelle = new SchnittstelleUdp(konfiguration.Ressource + ". " + konfiguration.Anwendung + "." + konfiguration.Identifikation, ip.ToString(), konfiguration.Ressource);
            schnittstelle.Speichern(konfiguration.OrdnerSchnittstellen + "/" + schnittstelle.Identifikation + ".json");

            structure.DatenmodelleEinhängenAusOrdner(konfiguration.OrdnerDatenmodelle);

            using (SHA256 sha256Hash = SHA256.Create())
            {
                foreach (var Dateneintrag in structure.dateneinträge)
                {
                    // ComputeHash - returns byte array
                    byte[] bytes = sha256Hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Dateneintrag.Key));

                    // Convert byte array to a string
                    UInt64 adr = 0;
                    adr = BitConverter.ToUInt64(bytes);

                    adressKartierung.Add(adr, Dateneintrag.Value);
                    eintragKartierung.Add(Dateneintrag.Value, adr);
                }
            }

            structure.Start();

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

            byte[] puffer = new byte[1024];
            int pos = 0;            
            
            while(true)
            {
                structure.Zustand.WertAusSpeicherLesen();

                if (ausfuehrungsmodell.AktuellerZustandModulAktivieren())
                {
                    var schritt_param = ausfuehrungsmodell.ParameterAktuellerZustand();
                    
                    switch (schritt_param)
                    {
                        case "E":
                        {
                            change_lock = true;
                            foreach (var aenderung in aenderungen)
                            {
                                try {
                                    aenderung.Key.WertAusBytes(aenderung.Value);
                                    aenderung.Key.WertInSpeicherSchreiben();
                                } catch {

                                }
                            }
                            aenderungen.Clear();
                            change_lock = false;
                            break;
                        }                        
                        case "A":
                        {
                            var updated = structure.Lesen();
                            updated.RemoveAll(item => structure.nichtVerteilen.Contains(item));

                            foreach (var entry in updated)
                            {
                                var eintrag_ = structure.dateneinträge[entry];

                                var address_ = BitConverter.GetBytes(eintragKartierung[eintrag_]);
                                byte[] value_ = null;

                                var t = eintrag_.type;

                                switch (t)
                                {
                                    case Datentypen.String: 
                                    case Datentypen.Objekt: {
                                        var value_content = System.Text.Encoding.UTF8.GetBytes((String)eintrag_.WertSerialisieren());
                                        var value_length = BitConverter.GetBytes((UInt16)value_content.Length); //auf zwei Bytes normieren!!! maximale länge dann 65535
                                        var value_length_container = new byte[2];
                                        Array.Copy(value_length, value_length_container, value_length_container.Length);
                                        value_ = new byte[value_content.Length + value_length_container.Length];
                                        Array.Copy(value_length_container, value_, length: value_length_container.Length);
                                        Array.Copy(value_content, 0, value_, value_length_container.Length, value_content.Length);
                                        break;
                                    }
                                    case Datentypen.Bool: value_ = BitConverter.GetBytes((bool)eintrag_.Wert); break;
                                }

                                if (value_ != null)
                                {
                                    Array.Copy(address_, 0, puffer, pos, address_.Length);
                                    pos+=address_.Length;
                                    Array.Copy(value_, 0, puffer, pos, value_.Length);
                                    pos+=value_.Length;
                                }
                            }

                            if (pos > 0)
                            {
                                if (enc != null)
                                {
                                    var conv = enc.Encrypt(puffer, pos);
                                    foreach (var target in targets)
                                    {
                                        udpSock.BeginSendTo(conv, 0, conv.Length, 0, target, null, null);
                                    }
                                } else {
                                    foreach (var target in targets)
                                    {
                                        udpSock.BeginSendTo(puffer, 0, pos, 0, target, null, null);
                                    }
                                }
                                
                                Array.Clear(puffer, 0, pos);
                                pos = 0;
                            }
                            break;
                        }
                    }

                    structure.Zustand++;
                    structure.Zustand.WertInSpeicherSchreiben();
                }
            }
        }
    }
}