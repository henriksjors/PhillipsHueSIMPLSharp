﻿using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Newtonsoft.Json.Linq;
using Crestron.SimplSharp.CrestronDataStore;

namespace HueLights
{
    public class InfoEventArgs : EventArgs
    {
        public String InfoType { get; set; }
        public String JsonData { get; set; }
    }

    public enum PayloadType
    {
        RoomOnOff,
		BulbOnOff,
        Lvl,
        XY,
        Scene
    }

    public class Payload
    {
        private string _settype, _cmdtype;
        public string OnOff { get; set; }
        public ushort Lvl { get; set; }
        public string LvlType { get; set; }
        public string Effect { get; set; }
        public ushort Xval { get; set; }
        public ushort Yval { get; set; }
        public string Scene { get; set; }
        public string SetType
        {
            get { return this._settype; }
            set
            {
                this._settype = value;
                if (value == "lights")
                    _cmdtype = "state";
                else if (value == "groups")
                    _cmdtype = "action";
            }
        }
        public string CmdType
        {
            get { return this._cmdtype; }

        }
    }

    public static class HueBridge
    {
        public static event EventHandler<InfoEventArgs> InfoReceived;  //event handler indicating data received 

	    public static bool LocalKey;
        public static bool Authorized;
        public static bool Populated;
        public static string BridgeIp;
        public static string BridgeApi;
	    private static string _url;
	    private static string _cmd;
	    private static string _response;

		public static Dictionary<string, HueBulb> HueBulbs = new Dictionary<string, HueBulb>();
        public static Dictionary<string, HueGroup> HueGroups = new Dictionary<string, HueGroup>();
		public static Dictionary<string, HueSensor> HueSensors = new Dictionary<string, HueSensor>();
		//public static List<IHueItem> HueBulbs = new List<IHueItem>();
        
        /// <summary>
        /// registers with bridge, authorizes a user based on API key from the pairing
        /// </summary>
        public static void Register()
        {
            CrestronConsole.PrintLine("registering with bridge...");
            try
            {
				_url = string.Format("http://{0}/api", BridgeIp);
	            _cmd = "{\"devicetype\":\"my_hue_app#crestron\"}";
                CrestronConsole.PrintLine("Connecting to HueBridge: {0}",_url);
				_response = HttpConnect.Instance.Request(_url, _cmd, Crestron.SimplSharp.Net.Http.RequestType.Post);
				CrestronConsole.PrintLine("HueBridge Answered with {0} bytes",_response.Length);
                if (_response.Contains("link button not pressed"))
                {
                    Authorized = false;
                    CrestronConsole.PrintLine("Registration incomplete press button and retry...");
                }
				else if (_response.Contains("username"))
				{
				    Authorized = true;
				    CrestronConsole.PrintLine("Registration found username...");
				    var data = JArray.Parse(_response);
				    BridgeApi = (String) data[0]["success"]["username"];
				    CrestronConsole.PrintLine("API key is {0}", BridgeApi);
				    if (CrestronDataStoreStatic.SetLocalStringValue("apikey", BridgeApi) != CrestronDataStore.CDS_ERROR.CDS_SUCCESS)
				        CrestronConsole.PrintLine("error storing apikey");
				    CrestronConsole.PrintLine("Bridge registration complete");
				}
            }
            catch (Exception e)
            {
                CrestronConsole.PrintLine("Exception is {0}", e);
            }
        }

        /// <summary>
        /// sets up datastore for storing the bridge API key
        /// </summary>
        public static void SetupDataStore()
        {
            try
            {
                var err = CrestronDataStoreStatic.InitCrestronDataStore();
				CrestronConsole.PrintLine("DataStore status: {0}", err);
                CrestronDataStoreStatic.GlobalAccess = CrestronDataStore.CSDAFLAGS.OWNERREADWRITE;
            }
            catch (Exception e)
            {
                CrestronConsole.PrintLine("Exception is {0}", e);
            }
        }

        /// <summary>
        /// pulls API key from datastore
        /// </summary>
        /// <returns></returns>
        public static string GetDataStore()
        {
            string temp;
	        if (CrestronDataStoreStatic.GetLocalStringValue("apikey", out temp) == CrestronDataStore.CDS_ERROR.CDS_SUCCESS && temp.Length > 30)
	        {
				//CrestronConsole.PrintLine("local key: {0}",temp);
		        LocalKey = true;
	        }
	        else
	        {
				CrestronConsole.PrintLine("No local API key stored");
				LocalKey = false;
	        }

            return temp;
        }

		/// <summary>
		/// resets the datastore
		/// </summary>
        public static void ResetDataStore()
		{
			if (CrestronDataStoreStatic.clearLocal("apikey") != CrestronDataStore.CDS_ERROR.CDS_SUCCESS)
				CrestronConsole.PrintLine("Error removing API key");
		}

        /// <summary>
        /// gets the IP of the local bridge, currently one bridge is supported
        /// </summary>
        /// <returns></returns>
        public static string GetIp()
        {
            try
            {
				_url = "https://discovery.meethue.com";
	            _response = HttpsConnect.Instance.Request(_url, null);
                /*
                 [{"id":"001788fffe2ad33b","internalipaddress":"172.22.131.242"}]
                 */
				JArray BridgeArray = JArray.Parse(_response);
                BridgeIp = (String)BridgeArray[0].SelectToken("internalipaddress");
                //BridgeApi = "U8FEH-CRuHFGxXe59pitg6UeyqGKWnMsqHef8oMt";
                CrestronConsole.PrintLine("Get IP of Bridge complete...");
            }
            catch (Exception e)
            {
                CrestronConsole.PrintLine("Exception is {0}", e);
            }
            return BridgeIp;
        }

        /// <summary>
        /// generic request for lights, groups, scenes returns a JSON string to be parsed
        /// </summary>
        /// <param name="infotype"></param>
        /// <returns></returns>
        public static void GetBridgeInfo(string infotype)
        {
            try
            {
				_url = string.Format("http://{0}/api/{1}/{2}", BridgeIp, BridgeApi, infotype);
				string json = HttpConnect.Instance.Request(_url, null, Crestron.SimplSharp.Net.Http.RequestType.Get);
				OnInfoReceived(infotype, json);
            }
            catch (Exception e)
            {
                CrestronConsole.PrintLine("Exception: {0}",e);
            }
        }

		/// <summary>
		/// determines command type, formats string and sends to HTTPConnect instance
		/// </summary>
		/// <param name="payloadtype"></param>
		/// <param name="payload"></param>
		/// <param name="setid"></param>
		/// <returns></returns>
        public static string SetCmd(PayloadType payloadtype, Payload payload, ushort setid)
        {
            _url = string.Format("http://{0}/api/{1}/{2}/{3}/{4}", BridgeIp, BridgeApi, payload.SetType, setid, payload.CmdType);
            switch (payloadtype)
            {
                case PayloadType.RoomOnOff:
                {
					_cmd = String.Format("{0}\"on\":{1},\"effect\":\"{2}\"{3}", '{', payload.OnOff, payload.Effect, '}');
                    break;
                }
				case PayloadType.BulbOnOff:
				{
					_cmd = String.Format("{0}\"on\":{1}{2}", '{', payload.OnOff,'}');
					break;
				}
                case PayloadType.Lvl:
                {
					_cmd = "{\"" + payload.LvlType + "\":" + payload.Lvl.ToString() + "}";
                    break;
                }
                case PayloadType.XY:
                {
                    decimal x = (decimal)payload.Xval / 100;
                    decimal y = (decimal)payload.Yval / 100;
					_cmd = "{\"xy\":[" + x.ToString() + "," + y.ToString() + "]}";
                    break;
                }
                case PayloadType.Scene:
                {
					_cmd = String.Format("{0}\"scene\":\"{1}\"{2}", '{', payload.Scene, '}');
                    break;
                }
            }
			return HttpConnect.Instance.Request(_url, _cmd, Crestron.SimplSharp.Net.Http.RequestType.Put);
        }

		/// <summary>
		/// raises the event for received data
		/// </summary>
		/// <param name="infotype"></param>
		/// <param name="jsondata"></param>
        static void OnInfoReceived(string infotype, string jsondata)
        {
            if(infotype != null)
                InfoReceived(null, new InfoEventArgs(){InfoType = infotype, JsonData = jsondata}); //event delegate
        }
    }
}