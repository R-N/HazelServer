using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;

using System.Security.Cryptography;

using Hazel;
using Hazel.Tcp;

using UnityEngine;

using MySql.Data.MySqlClient;


namespace HazelServer
{
	public class UserInfo{
		public int id;
		public int room = 0;
		public string session;
		public Connection connection;
		public string name = null;
		public UserInfo(){
		}
	}



	public static class Extensions{
		public static System.Random rng = new System.Random();
		public static void Shuffle<T>(this IList<T> list){
			int n = list.Count;
			while (n > 1) {
				n--;
				int k = rng.Next (n + 1);
				T value = list [k];
				list [k] = list [n];
				list [n] = value;
			}
		}
	}



	class MainClass
	{
		
		static Dictionary<Connection, UserInfo> userByConnection = new Dictionary<Connection, string>();
		static Dictionary<string, Connection> connectionBySession = new Dictionary<string, Connection>();

		static Dictionary<byte, Action<Connection, HazelReader>> DataHandlers = new Dictionary<byte, Action<Connection, HazelReader>> ();

		static ConnectionListener listener;

		static MySqlConnection dbconn;

		static HashSet<Task> taskKeepAlive = new HashSet<Task>();

		static string myConnectionString = "server=127.0.0.1;"+
			"port=2500;" +
			"uid=root;" +
			"pwd=Mojo1234;"+
			"database=mydb;";


		static HashAlgorithm md5;

		public static Dictionary<int, Room> rooms = new Dictionary<int, Room>();
		static int lastRoomNumber = 1;

		static string serverhash = null;

		static List<TcpConnectionListener> listeners = new List<TcpConnectionListener> ();

		public static void HashPass(string uname, string pass){
			System.Diagnostics.Debug.WriteLine(MD5 (uname + "2570" + MD5(pass + "134")));
		}

		public static void Main(string[] args)
		{
			md5 = ((HashAlgorithm)CryptoConfig.CreateFromName ("MD5"));


			//System.Diagnostics.Debug.WriteLine ("md5 " + MD5 ("linearch2570" + MD5("4qwh0lyg4n5134")));

			dbconn = new MySql.Data.MySqlClient.MySqlConnection(myConnectionString);
			dbconn.Open();

			AddDataHandler (0, Login);
			AddDataHandler (1, Relog);
			//2 reserved for register
			//3 reserved for character creation
			//4 reserved for logout
			AddDataHandler (5, RoomList);
			AddDataHandler (6, CreateRoom);
			AddDataHandler (7, JoinRoom);
			AddDataHandler (8, RoomStuff);



			/*int count = 0;
			for (int i = 3000; i < 10000; i++) {
				try{
					TcpConnectionListener l = new TcpConnectionListener(IPAddress.Any, i);
					l.Start ();
					listeners.Add(l);
					count++;
				}catch(Exception ex){
					Console.WriteLine ("Failed listening on : " + i);
					count--;
				}
			}
			Console.WriteLine ("Total listeners : " + count);*/


			listener = new TcpConnectionListener(IPAddress.Any, 2570);

			listener.NewConnection += NewConnectionHandler;


			listener.Start();

			Console.WriteLine("Server started!");
			Console.WriteLine ("Type 'exit' to exit");

			while (true) {
				string i = Console.ReadLine ();
				if (i == "exit")
					break;
			}


		}

		static bool AddDataHandler(byte id, Action<Connection, HazelReader> act){
			if (DataHandlers.ContainsKey (id))
				return false;
			DataHandlers.Add (id, act);
			return true;
		}

		static void NewConnectionHandler(object sender, NewConnectionEventArgs args)
		{
			args.Connection.DataReceived += DataReceivedHandler;
			args.Connection.Disconnected += DisconnectionHandler;
			Console.WriteLine("New connection from " + args.Connection.EndPoint.ToString());

			args.Recycle();
		}

		static void WaitReconnect (string session){
			Stopwatch sw = Stopwatch.StartNew ();
			while (sw.Elapsed.Seconds < 5 && connectionBySession [session].State != ConnectionState.Connected) {
			}
			Connection conn = connectionBySession [session];
			if (conn.State == ConnectionState.Connected) {
				Console.WriteLine(conn.EndPoint.ToString() + " has reconnected.");
			}else{
				Console.WriteLine (conn.EndPoint.ToString () + " disconnection timeout reached.");
				UserInfo info = userByConnection [conn];
				if (info.room > 0 && rooms.ContainsKey (info.room))
					rooms [info.room].Leave (conn);
			}
			userByConnection.Remove (conn);
			conn.Dispose ();
		}
	

		static void DisconnectionHandler(object sender, DisconnectedEventArgs args){
			Connection connection = (Connection)sender;
			Console.WriteLine(connection.EndPoint.ToString() + " disconnected. Waiting for him to reconnect.");
			if (userByConnection.ContainsKey (connection)) {
				Task t = new Task (() => WaitReconnect (userByConnection[connection].session));
				t.ContinueWith ((ta) => DisposeTask (ta));
				t.Start ();
			}
			args.Recycle ();

		}

		static void DisposeTask (Task t){
			taskKeepAlive.Remove (t);
			t.Dispose ();
		}

		private static void DataReceivedHandler(object sender, Hazel.DataReceivedEventArgs args)
		{
			Connection conn = (Connection)sender;
			HazelReader reader = new HazelReader (args.Bytes);

			byte header = reader.ReadByte ();

			if (!userByConnection.ContainsKey (conn) && header > 2)
				return;
			if (userByConnection[conn].name == null && header > 3)
				return;

			if (DataHandlers.ContainsKey(header))
			DataHandlers [header] (conn, reader);
			
			args.Recycle();
		}

		static void Login(Connection conn, HazelReader reader){
			string uname = reader.ReadString ();
			string pass = reader.ReadString ();

			string tryHash = MD5 (uname + "2570" + pass);

			MySqlCommand cmd = dbconn.CreateCommand ();
			cmd.CommandText = "SELECT id FROM accountinfo WHERE username = @uname AND passhash = @passhash";
			cmd.Parameters.AddWithValue ("@uname", uname);
			cmd.Parameters.AddWithValue ("@passhash", tryHash);
			cmd.Prepare ();
			MySqlDataReader sqlReader = cmd.ExecuteReader();
			HazelWriter writer = new HazelWriter ();
			writer.WriteByte(0);
			if (sqlReader.Read ()) {
				int plyrid = sqlReader.GetInt32 (0);
				string ip = conn.EndPoint.ToString ().Split(':')[0];
				string sessionkey = MD5 (uname + DateTime.UtcNow.Ticks + ip);
				string session = GenerateSession (sessionkey, conn);
				userByConnection.Add (conn, new UserInfo (){ id = plyrid, connection = conn });
				connectionBySession.Add (session, conn);

				writer.Write (sessionkey);
			} else {
				writer.Write (string.Empty);
			}
			sqlReader.Close ();
			sqlReader.Dispose ();
			cmd.Dispose ();
			conn.SendBytes (writer.bytes);
		}

		public static string GenerateSession (string sessionKey, Connection conn){
			return MD5 (sessionKey + conn.EndPoint.ToString ().Split(':')[0]);
		}

		static void RoomStuff(Connection conn, HazelReader reader){
			
		}

		static void Relog(Connection conn, HazelReader reader){
			string ip = conn.EndPoint.ToString ().Split(':')[0];
			string sessionkey = reader.ReadString ();
			string session = GenerateSession (sessionkey, conn);

			if (connectionBySession.ContainsKey (session)) {
				Console.WriteLine (ip + " reconnected");
				userByConnection [connectionBySession [session]].connection = conn;
				connectionBySession [session].Close ();
				connectionBySession[session] = conn;
				UserInfo info = userByConnection [conn];
				if (info.room > 0 && rooms.ContainsKey (info.room)) {
					if (rooms [info.room].HasUser (info.id)) {
						Room room = rooms [info.room];
						room.AddUser (info, room.password);
					} else {
						info.room = 0;
					}
				}
			} else {
				Console.WriteLine (ip + " reconnect failed");
			}
		}

		static void MyUserInfo(Connection conn, HazelReader reader){
			HazelWriter writer = new HazelWriter ();
			writer.WriteByte (7);
			UserInfo info = userByConnection [conn];
			writer.Write (info.room);
			//other stuff
			conn.SendBytes (writer.bytes);
		}

		static void RoomList(Connection conn, HazelReader reader){
			HazelWriter writer = new HazelWriter ();
			writer.WriteByte(2);
			writer.Write (rooms.Count);
			IEnumerable<Room> openRooms = rooms.Values.Where (r => (!r.playing && r.players.Count < r.maxPlayers));
			foreach (Room r in openRooms) {
				writer.Write (r.number);
				writer.Write (r.name);
				writer.Write (!string.IsNullOrEmpty(r.password));
				writer.Write (r.playing);
				writer.Write (r.maxPlayers);
				writer.Write (r.players.Count);
			}

			conn.SendBytes (writer.bytes);
		}
		static void CreateRoom(Connection conn, HazelReader reader){
			UserInfo info = userByConnection [conn];
			HazelWriter writer = new HazelWriter ();
			writer.WriteByte(3);
			if (info.room > 0 || info.name == null) {
				writer.Write (-1);
				conn.SendBytes (writer.bytes);
				return;
			}
			while (rooms.ContainsKey (lastRoomNumber)) {
				lastRoomNumber++;
			}
			int roomNumber = lastRoomNumber;
			string password = reader.ReadString ();
			rooms.Add(roomNumber, new Room(roomNumber, reader.ReadString()){
				type = reader.ReadInt(),
				maxPlayers = reader.ReadInt(),
				password = reader.ReadString()
			});
			int id = rooms [roomNumber].AddUser (info, password);
			info.room = roomNumber;
			Console.WriteLine(conn.EndPoint.ToString() + " created room " + roomNumber);
			writer.Write (roomNumber);
			writer.Write (id);
			conn.SendBytes (writer.bytes);
		}
		static void JoinRoom(Connection conn, HazelReader reader){
			UserInfo info = userByConnection [conn];
			HazelWriter writer = new HazelWriter ();
			writer.WriteByte(3);
			int roomNumber = reader.ReadInt ();
			if (info.name == null || (info.room > 0 && info.room != roomNumber)) {
				writer.Write (-1);
				conn.SendBytes (writer.bytes);
				return;
			}
			if (!rooms.ContainsKey (roomNumber)) {
				writer.Write (-2);
				conn.SendBytes (writer.bytes);
				RoomList (conn, reader);
				return;
			}
			string password = reader.ReadString ();
			int id = rooms [roomNumber].AddUser (info, password);
			if (id > -1) {
				//join successful
				info.room = roomNumber;
				writer.Write (roomNumber);
				writer.Write (id);
				conn.SendBytes (writer.bytes);
			} else {
				writer.Write (id);
				RoomList (conn, reader);
				conn.SendBytes (writer.bytes);
			}
		}


		static void LeaveRoom(Connection conn, HazelReader reader){
			UserInfo info = userByConnection [conn];
			if (info.room <= 0)
				return;
			if (rooms [info.room].playing)
				return;
			if (rooms [info.room].HasPlayer (info.id))
				rooms [info.room].Leave (conn);
			info.room = 0;
		}

		static string MD5(string input){
			// byte array representation of that string
			byte[] temp5 = new UTF8Encoding().GetBytes(input);
			// need MD5 to calculate the hash
			byte[] hash = md5.ComputeHash(temp5);

			return BitConverter.ToString(hash)
				// without dashes
					.Replace("-", string.Empty);
		}
	}
}
