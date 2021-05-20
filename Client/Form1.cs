using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Client
{
	public partial class Form1 : Form
	{
		// Klasa zawierająca dane rysunkowe poszczególnego klienta
		class PaintData
		{
			public Color Color { get; set; }
			public Point StartPos { get; set; }

			public PaintData(Color color, Point point)
			{
				Color = color;
				StartPos = point;
			}
		};

		UdpClient udpClient;
		IPEndPoint udpEndPoint;
		Color myColor;
		bool connected;
		Dictionary<byte, PaintData> udpClients;
		Task receiveTask;

		/*
		 * Jako klient UDP potrzebujemy tylko adresu docelowego i portu, end of story.
		 */
		public Form1()
		{
			//System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en-us"); // Pozwala włączyć angielskie komunikaty rzucanych wyjątków
			InitializeComponent();
			pictureBox1.Image = new Bitmap(pictureBox1.Width, pictureBox1.Height);
			
			udpClient = new UdpClient();					// Socket z losowym portem
			myColor = Color.Black;							// Początkowy kolor
			connected = false;								// Status połączenia
			udpClients = new Dictionary<byte, PaintData>(); // Lista danych rysunkowych klientów
		}

		private void btnConnect_Click(object sender, EventArgs e)
		{
			if(connected == false)
			{
				// Najpierw "łączymy się", a.k.a. ustawiamy domyślny IPEndPoint (binding)
				// Dzięki temu nie musimy sami zapamiętywać go i możemy wykorzystać 2 argumentowy Send()
				udpClient.Connect(txtIP.Text, (int) numPort.Value);
				udpClient.Send(Encoding.ASCII.GetBytes("connect"), 7);

				// Łączymy się z serwerem "połączeniowym" i pobieramy od niego adres serwera rysunkowego
				udpEndPoint = new IPEndPoint(IPAddress.Any, 0);
				var data = udpClient.Receive(ref udpEndPoint);
				udpEndPoint.Port = BitConverter.ToInt16(data, 0);

				// Teraz bindujemy się do serwera danych rysunkowych i wywołujemy wątek odbiorczy
				txtStatus.Text = "connected";
				udpClient.Connect(udpEndPoint);
				connected = true;
				receiveTask = Task.Run(new Action(watchForData));
			}
		}

		private void btnDisconnect_Click(object sender, EventArgs e)
		{
			if (connected == true)
			{
				// "Kończymy" połączenie i następnie zamykamy socket.
				udpClient.Connect(txtIP.Text, (int) numPort.Value);
				udpClient.Send(Encoding.ASCII.GetBytes("disconnect"), 10);
				udpClient.Close();

				// Czekamy na zakończenie wątku odbiorczego.
				// Zapewne będzie blokowany przez Receive(), dlatego musimy zamknąć socket, na którym działa.
				// Zostanie wyrzucony wyjątek, a praca wątku nie będzie kontynuowana. (safe exit)
				connected = false;
				receiveTask.Wait();

				// Z zamkniętemy socketem nic nie zrobimy, pozostaje nam utworzenie nowego
				udpClient = new UdpClient();
				txtStatus.Text = "disconnected";
			}
		}

		void watchForData()
		{
			try
			{
				while (connected == true)
				{
					// Tutaj pobieramy dane rysownicze od serwera i coś tam robione jest
					IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
					byte[] receivedBytes = udpClient.Receive(ref remoteEndPoint);

					byte id = receivedBytes[0];
					switch (receivedBytes[1])
					{
						case 0x01:
						{
							byte[] color = new byte[4];
							Buffer.BlockCopy(receivedBytes, 2, color, 0, color.Length);
							udpClients[id] = new PaintData(Color.FromArgb(BitConverter.ToInt32(color, 0)), new Point(0));
							break;
						}

						case 0x02:
						{
							try
							{
								var client = udpClients[id];
								byte[] pos = new byte[4];
								Buffer.BlockCopy(receivedBytes, 2, pos, 0, pos.Length);
								Point newPos = new Point(BitConverter.ToInt32(pos, 0));
								
								if (client.StartPos.IsEmpty == true)
									client.StartPos = newPos;

								using (Pen p = new Pen(client.Color, 5.0F))
								{
									// Działamy na osobnym wątku, dlatego rysowanie na kontrolce
									// musimy albo zlecić, albo wykonać od razu.
									// Jeśli wykonamy od razu, to jest szansa na "wpadkę" i wyjątek, dlatego
									// zlecamy jeśli jest to potrzebne
									if(pictureBox1.InvokeRequired)
									{
										pictureBox1.Invoke(new Action(() =>
										{
											var graphics = Graphics.FromImage(pictureBox1.Image);
											graphics.DrawLine(p, client.StartPos, newPos);
											pictureBox1.Invalidate();
										}));
									}
									else
									{
										var graphics = Graphics.FromImage(pictureBox1.Image);
										graphics.DrawLine(p, client.StartPos, newPos);
										pictureBox1.Invalidate();
									}
								}
								client.StartPos = newPos;
							}
							catch(KeyNotFoundException) {}
							break;
						}

						case 0x03:
						{
							try
							{
								var client = udpClients[id];
								client.StartPos = new Point(0);
							}
							catch (KeyNotFoundException) {}
							break;
						}
					}
				}
			}
			catch (SocketException e)
			{
				if(connected == true)
					MessageBox.Show(e.Message + "\n" + e.StackTrace);
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				Console.WriteLine(e.StackTrace);
				MessageBox.Show(e.Message + "\n" + e.StackTrace);
			}
		}

		private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
		{
			if (connected == false)
				return;

			// Pobieranie koloru i wysłanie stosownego komunikatu
			byte[] data = new byte[5];
			data[0] = 0x01;
			Buffer.BlockCopy(BitConverter.GetBytes(myColor.ToArgb()), 0, data, 1, sizeof(int));
			udpClient.Send(data, data.Length);
		}

		private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
		{
			if (connected == false)
				return;

			if (e.Button != MouseButtons.Left)
				return;
			
			// Pobranie lokacji kursora i wysłanie stosownego komunikatu
			byte[] data = new byte[5];
			data[0] = 0x02;
			Buffer.BlockCopy(BitConverter.GetBytes((short) e.Location.X), 0, data, 1, 2);
			Buffer.BlockCopy(BitConverter.GetBytes((short) e.Location.Y), 0, data, 3, 2);
			udpClient.Send(data, data.Length);
		}

		private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
		{
			if (connected == false)
				return;

			// Po zwolnieniu myszki wysyłamy stosowny komunikat
			byte[] data = new byte[1];
			data[0] = 0x03;
			udpClient.Send(data, 1);
		}

		private void btnColor_Click(object sender, EventArgs e)
		{
			if(dlgColorpicker.ShowDialog() == DialogResult.OK)
			{
				myColor = dlgColorpicker.Color;
			}
		}
	}
}
