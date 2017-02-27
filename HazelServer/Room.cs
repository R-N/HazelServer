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
	public class Player{
		public int id;
		public int userId;
		public Connection conn;
		public int hero;
		public string name;
		public bool alive = true;
		public int3 position;
		  bool _doneTurn = true;
		public int prevHeight = 0;
		public int team = 0;

		public bool doneTurn {
			get {
				return _doneTurn || !alive;
			}
			set {
				_doneTurn = value || !alive;
			}
		}

		public int suit = 0;
	}


	public class Platform{
		public int3 position;
	}

	public struct SpecialTile{
		public int id;
		public int3 position;
	}

	public class Room{
		
		public int number;
		public string name;

		public string password;

		public Dictionary<int, Player> playerById = new Dictionary<int, Player>();
		public Dictionary<int, Player> playerByUserId = new Dictionary<int, Player>();
		public Dictionary<Connection, Player> playerByConnection = new Dictionary<Connection, Player> ();

		public List<Player> players = new List<Player>();

		public List<Platform> platforms = new List<Platform>();

		public List<SpecialTile> specialTiles;

		public int maxPlayers = 4;

		public int alivePlayers = 0;

		public Task game = null;

		public Dictionary<int, int> suits = new Dictionary<int, int>();

		public Dictionary<int, Platform> pendingMove = new Dictionary<int, Platform> ();

		public int turnRemaining = 0;

		public int it = -1;

		public Connection master = null;

		public int type = 0;

		public int lastId = 0;


		public bool playing {
			get {
				return (game != null && game.Status == TaskStatus.Running);
			}
		}

		public Room(int number, string name){
			this.number = number;
			this.name = name;
		}

		public void Start(int id){
			if (master == id) {
				game = new Task (() => GameLoop ());
				game.Start ();
			}
		}


		public void GameLoop(){

			Stopwatch sw = new Stopwatch ();
			while (players.Count > 1) {
				players.Shuffle ();
				int n = players.Count;
				for (int i = 0; i < n; i++) {
					if (players [i].alive) {
						sw.Restart ();
						players [i].doneTurn = false;
						while (!players [i].doneTurn && sw.Elapsed.Seconds < 5) {

						}
						players [i].doneTurn = true;
					}
				}

				for (int i = 0; i < n; i++) {
					players [i].suit = 0;
					players [i].doneTurn = false;
				}

				while (it == -1) {
					turnRemaining = 0;
					for (int i = 0; i < n; i++) {
						if (!players [i].doneTurn)
							turnRemaining++;
					}
					suits.Clear ();
					int startingTurn = turnRemaining;

					if (startingTurn == 1) {
						for (int i = 0; i < n; i++) {
							if (!players[i].doneTurn)
								it = players [i].id;
						}
						break;
					}


					//set doneturn to true for those who lose
					if (startingTurn > 2) {
						sw.Restart ();
						while (turnRemaining > 0 && sw.Elapsed.Seconds < 5) {

						}
						int black = 0;
						int white = 0;
						for (int i = 0; i < n; i++) {
							if (players [i].suit == 1)
								black++;
							else if (players [i].suit == 2)
								white++;
						}

						for (int i = 0; i < n; i++) {
							if (!players [i].doneTurn && players [i].suit == 0)
								players [i].doneTurn = true;
						}
						if (black > white) {
							for (int i = 0; i < n; i++) {
								if (players [i].suit == 1)
									players [i].doneTurn = true;
							}
						} else if (black < white) {
							for (int i = 0; i < n; i++) {
								if (players [i].suit == 2)
									players [i].doneTurn = true;
							}
						}
						for (int i = 0; i < n; i++) {
							players [i].suit = 0;
						}
					} else {
						Player a = null;
						Player b = null;

						for (int i = 0; i < n; i++) {
							if (!players [i].doneTurn) {
								if (a == null)
									a = players [i];
								else
									b = players [i];
							}
						}

						a.suit = 0;
						b.suit = 0;
						while (a.suit == b.suit) {
							sw.Restart ();
							while ((a.suit == 0 || b.suit == 0) && sw.Elapsed.Seconds < 5) {

							}
						}

						if (a.suit == 0) {
							it = b.id;
						} else if (b.suit == 0) {
							it = a.id;
						} else if (a.suit != 1 && b.suit > a.suit) {
							it = b.id;
						} else if (b.suit != 1 && a.suit > b.suit) {
							it = a.id;
						} else if (a.suit == 1 && b.suit == 3) {
							it = a.id;
						} else if (b.suit == 1 && a.suit == 3) {
							it = b.id;
						}
						break;
					}
				}

				n = players.Count;
				turnRemaining = 0;
				for (int i = 0; i < n; i++) {
					if (players [i].alive) {
						turnRemaining++;
						players [i].doneTurn = false;
					}
				}

				pendingMove.Clear ();

				sw.Restart ();
				while (turnRemaining > 0 && sw.Elapsed.Seconds < 5) {

				}

				foreach (int k in pendingMove.Keys) {
					UpdateTransform (playerById [k], pendingMove [k]);
				}

				n = players.Count;

				//if there are players other than who's it in the same position as who's it, kill the other players

				int itTeam = playerById [it].team;

				int3 itPos = playerById [it].position;
				IEnumerable<Player> testit;
				if (itTeam == 0)
					testit = players.Where (p => (p.alive && p.id != it && p.position == itPos));
				else
					testit = players.Where (p => (p.alive && p.team != itTeam && p.position == itPos));

				foreach (Player p in testit) {
					p.alive = false;
				}


				//if there are several players on the same position which came from different height, kill one that came from lower height
				for (int i = 0; i < n; i++) {
					if (players [i].alive) {
						Player p0 = players [i];
						IEnumerable<Player> test3;
						if (p0.team == 0)
							test3 = players.Where (p => (p.alive && p.prevHeight < p0.prevHeight && p.position == p0.position ));
						else
							test3 = players.Where (p => (p.alive && p.team != p0.team && p.prevHeight < p0.prevHeight && p.position == p0.position ));

						foreach (Player p in test3) {
							p.alive = false;
						}
					}
				}

				//if there are more than 2 players in the same place, kill em all.
				for (int i = 0; i < n; i++) {
					Player p0 = players [i];
					if (players [i].alive) {
						IEnumerable<Player> test3 = players.Where (p => (p.position == p0.position && p.alive));
						if (test3.Count () > 2) {
							foreach (Player p in test3) {
								p.alive = false;
							}
							//and remove the platform
							platforms.Remove(platforms.SingleOrDefault(w => w.position ==  p0.position));
						}
					}
				}

				//if there are special tiles under some players, activate em

			}

		}


		public void PlayerCount(Connection conn, HazelReader reader){
			HazelWriter writer = new HazelWriter ();
			writer.WriteByte(5);

			int count = players.Count;
			writer.Write (count);
			conn.SendBytes (writer.bytes);
			if (count == 0) {
				MainClass.rooms.Remove (number);
			}

		}

		public void PlayerList(Connection conn, HazelReader reader){
			HazelWriter writer = new HazelWriter ();
			writer.WriteByte(4);
			int count = players.Count;
			writer.Write (count);
			if (count == 0) {
				MainClass.rooms.Remove (number);
				conn.SendBytes (writer.bytes);
				return;
			}
			foreach (Player p in players) {
				writer.Write (p.name);
			}
			conn.SendBytes (writer.bytes);

		}

		public void PlayerListByTeam(Connection conn, HazelReader reader){
			HazelWriter writer = new HazelWriter ();
			writer.WriteByte(6);

			int count = players.Count;
			writer.Write (count);
			if (count == 0) {
				MainClass.rooms.Remove (number);
				conn.SendBytes (writer.bytes);
				return;
			}

			IEnumerable<IGrouping<int, Player>> teams = players.GroupBy(p => p.team);
			writer.Write (teams.Count ());
			foreach (IGrouping<int, Player> team in teams) {
				writer.Write (team.Key);
				writer.Write (team.Count ());
				foreach (Player p in team) {
					writer.Write (p.name);
				}
			}
			conn.SendBytes (writer.bytes);
		}

		public int AddUser(UserInfo info, string password = null, int hero = 0){
			if (playerByUserId.ContainsKey (info.id)) {
				Player p = playerByUserId [info.id];
				if(playerByConnection.ContainsKey(p.conn))
					playerByConnection.Remove (p.conn);
				p.conn = info.connection;
				return p.id;
			}
			if (playing)
				return -3;
			if (players.Count >= maxPlayers)
				return -4;
			if (!string.IsNullOrEmpty (this.password) && this.password != password)
				return -5;

			Player p = new Player(){
				userId = info.id,
				hero = hero
			};

			lastId++;
			p.id = lastId;
			players.Add (p);
			playerById.Add (p.id, p);
			return p.id;

		}

		public bool HasUser(int userId){
			return playerByUserId.ContainsKey (userId);
		}
		public bool HasPlayer(int id){
			return playerById.ContainsKey (id);
		}

		public void Leave(Connection conn){
			if (!playerByConnection.ContainsKey (conn))
				return;
			
			if (!playing) {
				Player p = playerByConnection[conn];
				if (players.Count <= 1) {
					MainClass.rooms.Remove (number);
				} else {
					if (master == conn) {
						ChangeMaster (players [0].id);
					}
					if (p.id == lastId)
						lastId--;
				}

				players.Remove (p);
				playerById.Remove (p.id);
				playerByConnection.Remove (p.id);
			}

				playerByConnection.Remove (conn);
		}

		public void ChangeMaster(Connection conn){
			master = conn;
		}

		void UpdateTransform(Player p, Platform dest){
			p.prevHeight = p.position.y;
			p.position = dest.position;
		}

		public void Move(Connection conn, HazelReader reader){

			Player p = playerByConnection [conn];
			int3 targetPosition = reader.ReadInt3 ();
			bool pending = reader.ReadBool;
			if (!p.doneTurn && targetPosition.y <= p.position.y){
				//and it is within 1 block range from him, 
				int3 absDeltaPos = targetPosition - p.position;
				absDeltaPos = new int3 (Mathf.Abs (absDeltaPos.x), Mathf.Abs (absDeltaPos.y), Mathf.Abs(absDeltaPos.z));
				if ((absDeltaPos.y == 0 && absDeltaPos.x == 2) || (absDeltaPos.x == 1 && absDeltaPos.y == 1)){
					//and target platform available

					Platform dest = platforms.SingleOrDefault (w => w.position == targetPosition);

					if (dest != null){
						//and there are no player nor wall on the targetPosition, 
						if (!players.Exists(p2 => p2.position.xz == targetPosition.xz && targetPosition.y == p.position.y)){
							//turn done
							playerById[id].doneTurn = true;
							turnRemaining--;
							//move
							if (pending)
								pendingMove.Add (id, dest);
							else
								UpdateTransform (p, dest);
							
						}
					}
				}
			}
		}


		public void Jump(int id, int3 targetPosition){
			//if he's it and hasnt done his turn
			Player p = playerById[id];
			if (it == id && !p.doneTurn){
				bool validJump = false;
				int3 deltaPos = targetPosition - p.position;
				int3 absDeltaPos = new int3 (Mathf.Abs (deltaPos.x), Mathf.Abs (deltaPos.y), Mathf.Abs(deltaPos.z));
				
				//and destination platform available
				Platform dest = platforms.SingleOrDefault (w => w.position == targetPosition);
				if (dest != null){
					//and it is within 1 block range from him
					if ((absDeltaPos.y == 0 && absDeltaPos.x == 2) || (absDeltaPos.x == 1 && absDeltaPos.y == 1)) {
						if (targetPosition.y - p.position.y < 1)
							validJump = true;
						//or it is within 2 blocks range straight from him
					} else if ((absDeltaPos.y == 2 && (absDeltaPos.x == 0 || absDeltaPos.x == 2))
						|| (absDeltaPos.y == 0 && absDeltaPos.x == 4)) {
						//and there are no wall blocking the path to targetPosition
						int2 pos = (playerById [id].position.xz + new int2(deltaPos.x / 2, deltaPos.z / 2));
						if (!platforms.Exists(w => w.position.xz == pos && w.position.y - p.position.y == 2))
							validJump = true;

						//or it is within 2 blocks range not straight from him
					} else if (absDeltaPos.x == 3 && absDeltaPos.y == 1) {
						//and there are no wall blocking the path to targetPosition
						int2 pos1 = playerById[id].position.xz + new int2(deltaPos.x / 3, deltaPos.z);
						int2 pos2 = playerById [id].position.xz + new int2 (2 * deltaPos.x / 3, 0);

						if (!platforms.Exists(w => (w.position.xz == pos1 || w.position.xz == pos2) && w.position.y - p.position.y == 2))
							validJump = true;
					}
				}

				if (validJump){
					//turn done
					p.doneTurn = true;
					turnRemaining--;
					//jump
					pendingMove.Add (id, dest);
				}
			}
		}

		public void Suit(int id, int suit){
			//if he has suit turn

			Player p = playerById[id];
			if (!p.doneTurn) {
				p.doneTurn = true;
				turnRemaining--;
				//suit
				p.suit = suit;
			}
		}

	}
}

