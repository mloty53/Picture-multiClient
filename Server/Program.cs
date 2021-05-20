using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
	class Program
	{
		UdpClient udpConnServer;
		UdpClient udpPaintServer;
		List<IPEndPoint> udpClients;
		BlockingCollection<KeyValuePair<byte, byte[]>> paintData;

		/*
		 * UDP jest connection-less i nie zawiera za dużo rzeczy w sobie.
		 * a.k.a. surowa wersja TCP, gdzie my zarządzamy po swojemu "klientami",
		 * które są zwyczajnymi socketami.
		 * 
		 * Dowolny klient może się z nami skomunikować, my musimy określić kto
		 * chce się "połączyć" z naszym serwerem przy pomocy datagramów.
		 * 
		 * W tym przypadku są to komendy connect/disconnect. Adresy, które się z nami
		 * skomunikują przechowujemy sami w dowolny sposób (w tym przypadku List<IPEndPoint>)
		 */
		static void Main(string[] args)
		{
			Program p = new Program();
			p.udpConnServer = new UdpClient(1523);							// Serwer nasłuchu połączeń
			p.udpPaintServer = new UdpClient(11144);						// Serwer nasłuchu danych rysowniczych
			p.udpClients = new List<IPEndPoint>();							// Lista klientów (adresy)
			p.paintData = new BlockingCollection<KeyValuePair<byte, byte[]>>(new ConcurrentQueue<KeyValuePair<byte, byte[]>>(), 20); // Kolejka blokująca po osięgnięciu docelowej ilości
																																	 // elementów znajdujących się w tym momencie
																																	 // (a.k.a. ostatnia laba z Javy u Matuszexa™)

			Task waitingTask = Task.Run(new Action(p.watchForClients));		// Wątek nasłuchu połączeń (komendy connect/disconnect)
			Task receiveTask = Task.Run(new Action(p.watchForPaintData));	// Wątek nasłuchu danych rysowniczych (wybór koloru, punkty, wrzucenie tych danych do kolejki)
			Task sendTask = Task.Run(new Action(p.paintDataSender));		// Wątek przesyłu danych w/w do zarejestrowanych klientów (wyciąg z kolejki, przesył)

			Console.WriteLine("Server started on port {0}", ((IPEndPoint) p.udpConnServer.Client.LocalEndPoint).Port);
		
			while(true)
			{
				Thread.Sleep(1000); // Bez tego apka się wyłączy, można dorobić tutaj fancy komendy tekstowe
			}
		}

		void watchForClients()
		{
			try
			{
				while(true)
				{
					// Najprostszy kod na odbieranie - czekamy, aż coś dostaniemy z dowolnego adresu IP
					// Po odebraniu `ep` zostanie zaktualizowane danymi adresowymi klienta
					IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
					byte[] receivedBytes = udpConnServer.Receive(ref ep);

					// Konwersja datagramu na string
					string msg = Encoding.ASCII.GetString(receivedBytes);
					Console.WriteLine("> {0}", msg);
					if(msg == "connect")
					{	
						Console.WriteLine("{0} connected\n", ep);
						// Dodajemy klienta do listy i przesyłamy mu port serwera odbiorczego danych rysunkowych.
						// Jego identyfikatorem staje się indeks jego adresu
						udpClients.Add(ep);
						byte[] m = BitConverter.GetBytes((short)((IPEndPoint) this.udpPaintServer.Client.LocalEndPoint).Port);
						udpConnServer.Send(m, m.Length, ep);
					}
					else if(msg == "disconnect")
					{
						// Usuwanie klienta z listy
						udpClients.Remove(udpClients.Find(x => x.Equals(ep)));
						Console.WriteLine("{0} disconnected\n", ep);
					}
				}
			}
			catch(Exception e)
			{
				Console.WriteLine(e.Message);
				Console.WriteLine(e.StackTrace);
			}
		}

		void watchForPaintData()
		{
			try
			{
				while(true)
				{
					// Znowu, czekamy na dowolne dane, następnie sprawdzamy, czy adres tego klienta
					// znajduje się na liście. Jeśli nie, to nasłuchujemy od nowa.
					IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
					byte[] receivedBytes = udpPaintServer.Receive(ref remoteEndPoint);
					var client = udpClients.FindIndex(x => x.Equals(remoteEndPoint));
					if(client == -1)
					{
						Console.WriteLine("Unknown client {0}", remoteEndPoint);
						continue;
					}

					// Pierwszy bajt jest bajtem informującym nas o rodzaju przesyłanych danych
					// 1 - Wybrany kolor (jednocześnie jest to informacja o rozpoczęciu rysowania)
					// 2 - Nowy punkt na obrazie
					// 3 - Koniec rysowania
					switch (receivedBytes[0])
					{
						case 0x01: // Color
						{
							Console.WriteLine("{0} has started drawing", remoteEndPoint);
							break;
						}
						case 0x02:
						{
							Console.WriteLine("{0} sent point", remoteEndPoint);
							break;
						}
						case 0x03: // Stop
						{
							Console.WriteLine("{0} has stopped drawing", remoteEndPoint);
							break;
						}
						default:
							continue;
					}

					// Dodawanie danych rysunkowych do blokującej kolejki
					// ID klienta to jego indeks na liście klientów
					paintData.Add(new KeyValuePair<byte, byte[]>((byte) udpClients.FindIndex(x => x.Equals(remoteEndPoint)), receivedBytes));
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				Console.WriteLine(e.StackTrace);
			}
		}

		void paintDataSender()
		{
			try
			{
				while(true)
				{
					// Wybieramy dane rysunkowe, dopinamy id klienta,
					// który je wysłał i przesyłamy do wszystkich naszych klientów.
					var data = paintData.Take();
					byte[] toSendBytes = new byte[1 + data.Value.Length];
					toSendBytes[0] = data.Key;
					Buffer.BlockCopy(data.Value, 0, toSendBytes, 1, data.Value.Length);

					foreach (var client in udpClients)
					{
						udpPaintServer.Send(toSendBytes, toSendBytes.Length, client);
					}
				}
			}
			catch(Exception e)
			{
				Console.WriteLine(e.Message);
				Console.WriteLine(e.StackTrace);
			}
		}
	}
}
