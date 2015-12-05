﻿// COPYRIGHT 2012 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using Orts.Simulation.Physics;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ORTS.MultiPlayer
{
    public class OnlinePlayer
	{
		public Decoder decoder;
		public OnlinePlayer(TcpClient t, Server s) { Client = t; Server = s; decoder = new Decoder(); CreatedTime = Program.Simulator.GameTime; url = "NA";}// "http://trainsimchina.com/discuz/uc_server/avatar.php?uid=72965&size=middle"; }
		public TcpClient Client;
		public Server Server;
		public string Username = "";
		public string LeadingLocomotiveID = "";
		public Train Train;
		public string con;
		public string path; //pat and consist files
		public Thread thread;
		public double CreatedTime;
		private object lockObj = new object();
		public string url = ""; //avatar location
		public double quitTime = -100f;
		public enum Status {Valid, Quit, Removed};
		public Status status = Status.Valid;//is this player removed by the dispatcher
        public bool protect = false; //when in true, will not force this player out, to protect the one that others uses the same name

		public void Send(string msg)
		{
			if (msg == null) return;
			try
			{
				NetworkStream clientStream = Client.GetStream();

				lock (lockObj)//lock the buffer in case two threads want to write at once
				{
					byte[] buffer = Encoding.Unicode.GetBytes(msg);//encoder.GetBytes(msg);
					clientStream.Write(buffer, 0, buffer.Length);
					clientStream.Flush();
				}
			}
			catch
			{
			}
		}

		public void Receive(object client)
		{
			NetworkStream clientStream = Client.GetStream();

			byte[] message = new byte[8192];
			int bytesRead;
			int errorCount = 0;
			double firstErrorTick = 0;
			double nowTicks = 0;

			while (true)
			{
				//System.Threading.Thread.Sleep(Program.Random.Next(50, 200));

				bytesRead = 0;

				try
				{
					//blocks until a client sends a message
					bytesRead = clientStream.Read(message, 0, 8192);
				}
				catch
				{
					//a socket error has occured
					break;
				}

				if (bytesRead == 0)
				{
					//the client has disconnected from the server
					break;
				}

				//message has successfully been received
				string info = "";
				try
				{
					decoder.PushMsg(Encoding.Unicode.GetString(message, 0, bytesRead));//encoder.GetString(message, 0, bytesRead));
					info = decoder.GetMsg();
					while (info != null)
					{
						//System.Console.WriteLine(info);
						Message msg = Message.Decode(info);
						if (msg is MSGPlayer) ((MSGPlayer)msg).HandleMsg(this);
						else msg.HandleMsg();

						info = decoder.GetMsg();
					}
				}
				catch (MultiPlayerError)
				{
					break;
				}
				catch (SameNameError)
				{
					Client.Close();
					thread.Abort();
				}
				catch (Exception)
				{
					nowTicks = Program.Simulator.GameTime;
					if (firstErrorTick == 0)
					{
						firstErrorTick = nowTicks;
						errorCount = 1;
					}
					if (errorCount >= 5 && nowTicks - firstErrorTick < 10) //5 errors last 10 seconds
					{
						MSGMessage emsg = new MSGMessage(this.Username, "Error", "Too many errors received from you in a short period of time.");
						MPManager.BroadCast(emsg.ToString());
						break;
					}
					else if (errorCount < 5) { errorCount++; }
					else { firstErrorTick = nowTicks; errorCount = 0; }
					//System.Console.WriteLine(e.Message + info);
				}
			}

			System.Console.WriteLine("{0} quit", this.Username);
			if (Program.Simulator.Confirmer != null) Program.Simulator.Confirmer.Information(Program.Catalog.GetStringFmt("{0} quit.", this.Username));
			Client.Close();
			if (this.Train != null && this.status != Status.Removed) //remember the location of the train in case the player comes back later, if he is not removed by the dispatcher
			{
				if (!MPManager.Instance().lostPlayer.ContainsKey(this.Username)) MPManager.Instance().lostPlayer.Add(this.Username, this);
				this.quitTime = Program.Simulator.GameTime;
				this.Train.SpeedMpS = 0.0f;
				this.status = Status.Quit;
			}
			MPManager.Instance().AddRemovedPlayer(this);//add this player to be removed
			MPManager.BroadCast((new MSGQuit(this.Username)).ToString());
			thread.Abort();
		}

	}
}
