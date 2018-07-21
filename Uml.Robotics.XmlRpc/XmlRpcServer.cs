using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using System.Xml;

namespace Uml.Robotics.XmlRpc
{
    public class XmlRpcServer : XmlRpcSource
    {
        const string XMLRPC_VERSION = "XMLRPC++ 0.7";

        const string SYSTEM_MULTICALL = "system.multicall";
        const string METHODNAME = "methodName";
        const string PARAMS = "params";

        const string FAULTCODE = "faultCode";
        const string FAULTSTRING = "faultString";
        const string LIST_METHODS = "system.listMethods";
        const string METHOD_HELP = "system.methodHelp";
        const string MULTICALL = "system.multicall";

        private readonly ILogger logger = XmlRpcLogging.CreateLogger<XmlRpcServer>();
        private XmlRpcDispatch dispatcher = new XmlRpcDispatch();

        private int port;
        private bool introspectionEnabled;     // whether the introspection API is supported by this server
        private TcpListener listener;

        private XmlRpcServerMethod methodListMethods;
        private XmlRpcServerMethod methodHelp;
        private Dictionary<string, XmlRpcServerMethod> methods = new Dictionary<string, XmlRpcServerMethod>();

        public XmlRpcServer()
        {
            methodListMethods = new ListMethodsMethod(this);
            methodHelp = new HelpMethod(this);
        }

        public void Shutdown()
        {
            dispatcher.Clear();
            listener.Stop();
        }

        public int Port =>
            port;

        public XmlRpcDispatch Dispatch =>
            dispatcher;

        public void AddMethod(XmlRpcServerMethod method)
        {
            methods.Add(method.Name, method);
        }

        public void RemoveMethod(XmlRpcServerMethod method)
        {
            foreach (var rec in methods)
            {
                if (method == rec.Value)
                {
                    methods.Remove(rec.Key);
                    break;
                }
            }
        }

        public void RemoveMethod(string name) =>
            methods.Remove(name);

        public void Work(TimeSpan timeSlice) =>
            dispatcher.Work(timeSlice);

        public XmlRpcServerMethod FindMethod(string name)
        {
            if (methods.ContainsKey(name))
                return methods[name];
            return null;
        }

        public override Socket Socket =>
            listener?.Server;

        public bool BindAndListen(int port, int backlog = 5)
        {
            this.port = port;
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(backlog);
            this.port = ((IPEndPoint)listener.Server.LocalEndPoint).Port;
            dispatcher.AddSource(this, XmlRpcDispatch.EventType.ReadableEvent);

            logger.LogInformation("XmlRpcServer::bindAndListen: server listening on port {0}", this.port);

            return true;
        }

        // Handle input on the server socket by accepting the connection
        // and reading the rpc request.
        public override XmlRpcDispatch.EventType HandleEvent(XmlRpcDispatch.EventType eventType)
        {
            AcceptConnection();
            return XmlRpcDispatch.EventType.ReadableEvent;  // Continue to monitor this fd
        }

        // Accept a client connection request and create a connection to
        // handle method calls from the client.
        private void AcceptConnection()
        {
            while (listener.Pending())
            {
                try
                {
                    dispatcher.AddSource(new XmlRpcServerConnection(listener.AcceptSocketAsync().Result, this), XmlRpcDispatch.EventType.ReadableEvent);
                    logger.LogInformation("XmlRpcServer::acceptConnection: creating a connection");
                }
                catch (SocketException ex)
                {
                    logger.LogError(ex, "XmlRpcServer::acceptConnection: Could not accept connection ({0}).", ex.Message);
                    Thread.Sleep(10);
                }
            }
        }

        public void RemoveConnection(XmlRpcServerConnection sc)
        {
            dispatcher.RemoveSource(sc);
        }


        // Introspection support

        /// <summary>
        /// Specify whether introspection is enabled or not.
        /// </summary>
        public bool EnableIntrospection
        {
            set
            {
                if (introspectionEnabled == value)
                    return;

                introspectionEnabled = value;

                if (value)
                {
                    AddMethod(methodListMethods);
                    AddMethod(methodHelp);
                }
                else
                {
                    RemoveMethod(LIST_METHODS);
                    RemoveMethod(METHOD_HELP);
                }
            }
        }

        private void ListMethods(XmlRpcValue result)
        {
            result.SetArray(methods.Count + 1);

            int i = 0;
            foreach (var rec in methods)
            {
                result.Set(i++, rec.Key);
            }

            // Multicall support is built into XmlRpcServerConnection
            result.Set(i, MULTICALL);
        }

        // Run the method, generate response
        public string ExecuteRequest(string request)
        {
            string response = "";
            XmlRpcValue parms = new XmlRpcValue(), resultValue = new XmlRpcValue();
            string methodName = ParseRequest(parms, request);
            logger.LogWarning("XmlRpcServerConnection::ExecuteRequest: server calling method '{0}'", methodName);

            try
            {
                if (!ExecuteMethod(methodName, parms, resultValue) &&
                    !ExecuteMulticall(methodName, parms, resultValue))
                    response = GenerateFaultResponse(methodName + ": unknown method name");
                else
                    response = GenerateResponse(resultValue.ToXml());
            }
            catch (XmlRpcException fault)
            {
                logger.LogWarning(fault, "XmlRpcServerConnection::ExecuteRequest: fault {0}.", fault.Message);
                response = GenerateFaultResponse(fault.Message, fault.ErrorCode);
            }
            return response;
        }

        // Execute a named method with the specified params.
        public bool ExecuteMethod(string methodName, XmlRpcValue parms, XmlRpcValue result)
        {
            XmlRpcServerMethod method = FindMethod(methodName);

            if (method == null)
                return false;

            method.Execute(parms, result);

            // Ensure a valid result value
            if (!result.IsEmpty)
                result.Set("");

            return true;
        }

        // Create a response from results xml
        public string GenerateResponse(string resultXml)
        {
            const string RESPONSE_1 = "<?xml version=\"1.0\"?>\r\n<methodResponse><params><param>\r\n\t";
            const string RESPONSE_2 = "\r\n</param></params></methodResponse>\r\n";

            string body = RESPONSE_1 + resultXml + RESPONSE_2;
            string header = GenerateHeader(body);
            string result = header + body;
            logger.LogDebug("XmlRpcServerConnection::GenerateResponse:\n{0}\n", result);
            return result;
        }

        // Parse the method name and the argument values from the request.
        private string ParseRequest(XmlRpcValue parms, string request)
        {
            string methodName = "unknown";

            var requestDocument = XDocument.Parse(request);
            var methodCallElement = requestDocument.Element("methodCall");
            if (methodCallElement == null)
                throw new XmlRpcException("Expected <methodCall> element of XML-RPC is missing.");

            var methodNameElement = methodCallElement.Element("methodName");
            if (methodNameElement != null)
                methodName = methodNameElement.Value;

            var xmlParameters = methodCallElement.Element("params").Elements("param").ToList();

            if (xmlParameters.Count > 0)
            {
                parms.SetArray(xmlParameters.Count);

                for (int i = 0; i < xmlParameters.Count; i++)
                {
                    var value = new XmlRpcValue();
                    value.FromXElement(xmlParameters[i].Element("value"));
                    parms.Set(i, value);
                }
            }

            return methodName;
        }

        // Prepend http headers
        private string GenerateHeader(string body)
        {
            return string.Format(
                "HTTP/1.1 200 OK\r\n" +
                "Server: {0}\r\n" +
                "Content-Type: text/xml\r\n" +
                "Connection: Close\r\n" +
                "Content-length: {1}\r\n\r\n",
                XMLRPC_VERSION,
                XmlConvert.ToString(body.Length)
            );
        }

        public string GenerateFaultResponse(string errorMsg, int errorCode = -1)
        {
            const string bodyBegin = "<?xml version=\"1.0\"?>\r\n<methodResponse><fault>\r\n\t";
            const string bodyEnd = "\r\n</fault></methodResponse>\r\n";

            var faultStruct = new XmlRpcValue();
            faultStruct.Set(FAULTCODE, errorCode);
            faultStruct.Set(FAULTSTRING, errorMsg);
            string body = bodyBegin + faultStruct.ToXml() + bodyEnd;
            string header = GenerateHeader(body);

            return header + body;
        }

        // Execute multiple calls and return the results in an XML RPC array.
        public bool ExecuteMulticall(string methodNameRoot, XmlRpcValue parms, XmlRpcValue result)
        {
            if (methodNameRoot != SYSTEM_MULTICALL)
                return false;

            // There ought to be 1 parameter, an array of structs
            if (parms.Count != 1 || parms[0].Type != XmlRpcType.Array)
                throw new XmlRpcException(SYSTEM_MULTICALL + ": Invalid argument (expected an array)");

            int nc = parms[0].Count;
            result.SetArray(nc);

            for (int i = 0; i < nc; ++i)
            {
                if (!parms[0][i].HasMember(METHODNAME) ||
                    !parms[0][i].HasMember(PARAMS))
                {
                    result[i].Set(FAULTCODE, -1);
                    result[i].Set(FAULTSTRING, SYSTEM_MULTICALL + ": Invalid argument (expected a struct with members methodName and params)");
                    continue;
                }

                string methodName = parms[0][i][METHODNAME].GetString();
                XmlRpcValue methodParams = parms[0][i][PARAMS];

                XmlRpcValue resultValue = new XmlRpcValue();
                resultValue.SetArray(1);
                try
                {
                    if (!ExecuteMethod(methodName, methodParams, resultValue[0]) &&
                        !ExecuteMulticall(methodName, parms, resultValue[0]))
                    {
                        result[i].Set(FAULTCODE, -1);
                        result[i].Set(FAULTSTRING, methodName + ": unknown method name");
                    }
                    else
                    {
                        result[i] = resultValue;
                    }
                }
                catch (XmlRpcException fault)
                {
                    result[i].Set(FAULTCODE, 0);
                    result[i].Set(FAULTSTRING, fault.Message);
                }
            }

            return true;
        }

        private class ListMethodsMethod : XmlRpcServerMethod
        {
            public ListMethodsMethod(XmlRpcServer server)
                : base(server, LIST_METHODS)
            {
            }

            public override void Execute(XmlRpcValue parms, XmlRpcValue result)
            {
                this.Server.ListMethods(result);
            }

            public override string Help()
            {
                return "List all methods available on a server as an array of strings";
            }
        };

        /// <summary>
        /// Retrieve the help string for a named method
        /// </summary>
        private class HelpMethod : XmlRpcServerMethod
        {
            public HelpMethod(XmlRpcServer server)
                : base(server, METHOD_HELP)
            {
            }

            public override void Execute(XmlRpcValue parms, XmlRpcValue result)
            {
                if (parms[0].Type != XmlRpcType.String)
                    throw new XmlRpcException(METHOD_HELP + ": Invalid argument type");

                var method = this.Server.FindMethod(parms[0].GetString());
                if (method == null)
                    throw new XmlRpcException(METHOD_HELP + ": Unknown method name");

                result.Set(method.Help());
            }

            public override string Help()
            {
                return "Retrieve the help string for a named method";
            }
        };
    }
}
