using System;
using System.Collections.Generic;
using System.Text;

using ShelveIt.Domain;

using Newtonsoft.Json;

using UnityEngine.Networking;

namespace ShelveIt.Logic
{
	// Client of the community server (api/v1). Built on UnityWebRequest: TLS goes through Unity's own
	// stack, the timeout is a property, and the completion callback runs on the main thread, so the
	// UI bindings can be updated from it directly. Nothing here runs unless a trigger asks for it.
	public class CommunityClient
	{
		public const string kDefaultServer = "https://shelveit.servermartino.com";
		private const int kTimeoutSeconds = 10;

		public class Response
		{
			// "" when the request succeeded, otherwise "network", "timeout", "rate", "rejected",
			// "toolarge", "notfound", "forbidden" or "server".
			public string error = "";
			public string message = "";
			public long status;
			public string body = "";

			public bool Ok => error.Length == 0;

			public T Parse<T>() where T : class
			{
				try
				{
					return JsonConvert.DeserializeObject<T>(body);
				}
				catch (Exception ex)
				{
					Mod.Log.Warn("Community: unreadable response: " + ex.Message);
					return null;
				}
			}
		}

		private readonly Func<string> m_ServerUrl;

		public CommunityClient(Func<string> serverUrl)
		{
			m_ServerUrl = serverUrl;
		}

		public string BaseUrl
		{
			get
			{
				var url = (m_ServerUrl() ?? "").Trim();

				if (url.Length == 0)
				{
					url = kDefaultServer;
				}

				return url.TrimEnd('/') + "/api/v1";
			}
		}

		public void Health(Action<Response> done) => Send("GET", "/health", null, null, done);

		public void List(string query, string sort, int page, Action<Response> done)
		{
			var path = "/libraries?q=" + Uri.EscapeDataString(query ?? "") + "&sort=" + Uri.EscapeDataString(sort ?? "popular") + "&page=" + Math.Max(0, page);

			Send("GET", path, null, null, done);
		}

		public void Get(string id, bool download, Action<Response> done)
		{
			Send("GET", "/libraries/" + Uri.EscapeDataString(id) + (download ? "?download=1" : ""), null, null, done);
		}

		public void Publish(CommunityPayload payload, string author, string clientId, Action<Response> done)
		{
			Send("POST", "/libraries", JsonConvert.SerializeObject(payload, ShareCode.kJson), new Dictionary<string, string>
			{
				{ "X-Shelve-Author", Uri.EscapeDataString(author ?? "") },
				{ "X-Shelve-Client", clientId ?? "" },
			}, done);
		}

		public void Update(string serverId, string ownerToken, CommunityPayload payload, string author, string clientId, Action<Response> done)
		{
			Send("PUT", "/libraries/" + Uri.EscapeDataString(serverId), JsonConvert.SerializeObject(payload, ShareCode.kJson), new Dictionary<string, string>
			{
				{ "X-Shelve-Owner", ownerToken ?? "" },
				{ "X-Shelve-Author", Uri.EscapeDataString(author ?? "") },
				{ "X-Shelve-Client", clientId ?? "" },
			}, done);
		}

		public void Delete(string serverId, string ownerToken, Action<Response> done)
		{
			Send("DELETE", "/libraries/" + Uri.EscapeDataString(serverId), null, new Dictionary<string, string>
			{
				{ "X-Shelve-Owner", ownerToken ?? "" },
			}, done);
		}

		public void Report(string serverId, string reason, string note, string clientId, Action<Response> done)
		{
			var body = JsonConvert.SerializeObject(new ReportRequest { reason = reason, note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() }, ShareCode.kJson);

			Send("POST", "/libraries/" + Uri.EscapeDataString(serverId) + "/report", body, new Dictionary<string, string>
			{
				{ "X-Shelve-Client", clientId ?? "" },
			}, done);
		}

		private void Send(string method, string path, string json, Dictionary<string, string> headers, Action<Response> done)
		{
			UnityWebRequest request;
			var url = BaseUrl + path;

			try
			{
				request = new UnityWebRequest(url, method)
				{
					downloadHandler = new DownloadHandlerBuffer(),
					timeout = kTimeoutSeconds,
				};

				if (json != null)
				{
					request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) { contentType = "application/json" };
					request.SetRequestHeader("Content-Type", "application/json");
				}

				request.SetRequestHeader("Accept", "application/json");

				if (headers != null)
				{
					foreach (var pair in headers)
					{
						request.SetRequestHeader(pair.Key, pair.Value);
					}
				}
			}
			catch (Exception ex)
			{
				Mod.Log.Warn($"Community: {method} {path} not sent: {ex.Message}");
				done(new Response { error = "network", message = ex.Message });
				return;
			}

			UnityWebRequestAsyncOperation operation;

			try
			{
				operation = request.SendWebRequest();
			}
			catch (Exception ex)
			{
				request.Dispose();
				Mod.Log.Warn($"Community: {method} {path} not sent: {ex.Message}");
				done(new Response { error = "network", message = ex.Message });
				return;
			}

			operation.completed += _ =>
			{
				var response = new Response();

				try
				{
					response.status = request.responseCode;
					response.body = request.downloadHandler?.text ?? "";

					if (request.result == UnityWebRequest.Result.ConnectionError)
					{
						var error = request.error ?? "";

						response.error = error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 || error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ? "timeout" : "network";
						response.message = error;
					}
					else if (request.result != UnityWebRequest.Result.Success || response.status >= 400)
					{
						response.error = ErrorOf(response.status);
						response.message = ReadServerMessage(response.body) ?? request.error ?? "";
					}

					// Paths only: headers and bodies carry the owner token and are never logged.
					Mod.Log.Info($"Community: {method} {path.Split('?')[0]} -> {response.status}{(response.Ok ? "" : " " + response.error)}");
				}
				catch (Exception ex)
				{
					response.error = "network";
					response.message = ex.Message;
				}
				finally
				{
					request.Dispose();
				}

				try
				{
					done(response);
				}
				catch (Exception ex)
				{
					Mod.Log.Error("Community: response handler failed: " + ex);
				}
			};
		}

		private static string ErrorOf(long status)
		{
			switch (status)
			{
				case 401:
				case 403: return "forbidden";
				case 404: return "notfound";
				case 413: return "toolarge";
				case 422: return "rejected";
				case 429: return "rate";
				case 0: return "network";
				default: return "server";
			}
		}

		private static string ReadServerMessage(string body)
		{
			if (string.IsNullOrEmpty(body) || body[0] != '{')
			{
				return null;
			}

			try
			{
				return JsonConvert.DeserializeObject<ServerError>(body)?.message;
			}
			catch
			{
				return null;
			}
		}
	}
}
