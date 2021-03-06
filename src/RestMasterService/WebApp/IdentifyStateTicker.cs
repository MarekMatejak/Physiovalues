﻿using System;
//using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;

using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using NLog;
using RestMasterService.ComputationNodes;
using ServiceStack.Common.Net30;
//using ServiceStack.Net30.Collections.Concurrent;
using ServiceStack.Redis.Support;
using ServiceStack.ServiceHost;
using ServiceStack.WebHost.Endpoints;
using IdentificationAlgorithm;
using System.Diagnostics;
using SimulatorBalancerLibrary;
using IdentifyDTO = RestMasterService.ComputationNodes.IdentifyDTO;
using Worker = RestMasterService.ComputationNodes.Worker;

using PostSharp.Extensibility;

namespace RestMasterService.WebApp
{
    public class IdentifyStateTicker
    {
        // Singleton instance
        private readonly static IdentifyStateTicker _instance = new IdentifyStateTicker(GlobalHost.ConnectionManager.GetHubContext<IdentifyStateHub>().Clients);//convert to .net 3.5
        //        private readonly static Lazy<IdentifyStateTicker> _instance = new Lazy<IdentifyStateTicker> (new IdentifyStateTicker(GlobalHost.ConnectionManager.GetHubContext<IdentifyStateHub>().Clients));
        //private Timer _timer;
        private Thread computationThread;
        private readonly object _identifyStateLock = new object();
        private readonly object _updateIdentifyState = new object();
        private bool _updatingIdentifyState = false;
        //private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(2000);
        private OrderedDictionary <string, IdentifyParameters>  parameters = new OrderedDictionary<string,IdentifyParameters>();//Dictionary<string,double>() ;
        private string[] variable_names;
            //private ConcurrentQueue<string> variable_names = new ConcurrentQueue<string>();
        private double[][] variable_values;
        private string modelname = "mySinc";
        private double IAgenerations = 0;
        private double IApopulationsize = 0;
        private double IAtolfun = 0;
        //string masterserviceurl = "http://localhost/identifikace/";
        string masterserviceurl = "http://localhost:51382/";
        Random random = new Random();
        Logger logger = LogManager.GetLogger("MyClassName");
        //private readonly ConcurrentDictionary<string, Stock> _stocks = new ConcurrentDictionary<string, Stock>();
        //constructor

        private IdentifyStateTicker(IHubConnectionContext clients)
        {
            Clients = clients;
            //adds defaoult worker
            //TODO add it here ?
            var myrepository = ((AppHostBase)EndpointHost.AppHost).Container.Resolve<WorkersRepository>();
            var w = new Worker{ModelName = "mysinc",RestUrl = "mysinc",WorkerType="test"};
            myrepository.Store(w);
        }

        public static IdentifyStateTicker Instance
        {
            get
            {
                return _instance; //moved to .net framework 3.5
                //return _instance.Value;

            }
        }

        private IHubConnectionContext Clients
        {
            get;
            set;
        }

        private volatile IdentifyState _identifyState;
        public IdentifyState IdentifyState
        {
            get { return _identifyState; }
            private set { _identifyState = value; }
        }


        public void StartIdentify()
        {
            lock (_identifyStateLock)
            {
                if (IdentifyState != IdentifyState.Running)
                {
                    //initialize updater for clients
                    //_timer = new Timer(UpdateIdentifyState, null, _updateInterval, _updateInterval);
                    //start computation
                    computationThread = new Thread(IdentifyComputation);
                    computationThread.Start();
                    IdentifyState = IdentifyState.Running;

                    BroadcastIdentifyStateChange(IdentifyState.Running);
                }
            }
        }
        /*
        private static IEnumerable<KeyValuePair<string, string>> GetBindings(HttpContext context)
        {
            // Get the Site name  
            string siteName = System.Web.Hosting.HostingEnvironment.SiteName;

            // Get the sites section from the AppPool.config 
            Microsoft.Web.Administration.ConfigurationSection sitesSection =
                Microsoft.Web.Administration.WebConfigurationManager.GetSection(null, null, "system.applicationHost/sites");

            foreach (Microsoft.Web.Administration.ConfigurationElement site in sitesSection.GetCollection())
            {
                // Find the right Site 
                if (String.Equals((string)site["name"], siteName, StringComparison.OrdinalIgnoreCase))
                {

                    // For each binding see if they are http based and return the port and protocol 
                    foreach (Microsoft.Web.Administration.ConfigurationElement binding in site.GetCollection("bindings"))
                    {
                        string protocol = (string)binding["protocol"];
                        string bindingInfo = (string)binding["bindingInformation"];

                        if (protocol.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] parts = bindingInfo.Split(':');
                            if (parts.Length == 3)
                            {
                                string port = parts[1];
                                yield return new KeyValuePair<string, string>(protocol, port);
                            }
                        }
                    }
                }
            }
        }
        */
        
        public void IdentifyComputation()
        {
            //set generic simulator behavior - strategy
            //GenericSimulator.SimulatorBehavior = new MySincSimulatorBehavior2(); //useless - matlab DLL have different static instance
            try
            {
                BroadcastIdentifyStateMessage("Starting calculation ..." + DateTime.Now);
                //invoke matlab Identification algorithm - does it use the GenericSimulator instance configured before???
                Class1 class1 = new Class1();
                //set experiment variables and values
                MWArray v_names = new MWCellArray(new MWCharArray(variable_names.ToArray()));
                MWArray experiment = new MWNumericArray(variable_values);
                //set identification parameters
                MWArray p_names = new MWCellArray(new MWCharArray(parameters.Keys.ToArray()));
                //set worker nodes urls
                //var host = AppHostBase.Instance.Config.ServiceEndpointsMetadataConfig.DefaultMetadataUri;
                //var myrepository = ((AppHostBase) EndpointHost.AppHost).Container.Resolve<WorkersRepository>();
                //MWArray worker_urls = new MWCellArray(new MWCharArray(myrepository.GetByModelName(modelname).Select(x => x.RestUrl).ToArray()));

                MWArray p_val = new MWNumericArray(parameters.Values.Select(parameter => parameter.Value).ToArray());
                MWArray p_min = new MWNumericArray(parameters.Values.Select(parameter => parameter.Min).ToArray());
                MWArray p_max = new MWNumericArray(parameters.Values.Select(parameter => parameter.Max).ToArray());
                MWArray p_is_fixed =
                    new MWLogicalArray(parameters.Values.Select(parameter => !parameter.IsActive).ToArray());
                //calculate identification
                Stopwatch sw = Stopwatch.StartNew();
                if (IAgenerations > 0) //set gaoptions
                {
                    MWArray generations = new MWNumericArray(IAgenerations);
                    MWArray populationsize = new MWNumericArray(IApopulationsize);
                    MWArray tolfun = new MWNumericArray(IAtolfun);
                    class1.identify_gaoptimset(generations, populationsize, tolfun);
                } //otherwise the matlab algorithm has it's own default values
                var result = class1.identify_main(experiment, p_names, p_val, p_min, p_max, p_is_fixed, v_names,modelname,masterserviceurl);
                sw.Stop();
                
                /*var fitted_params = result[1];
                var fitted_param_L2Enorm = result[2];
                var fitted_variablevalues = result[3];
                 * */
                //string fittedparams = ""; 
                //for (int i=1;i < fitted_params.
                //BroadcastIdentifyStateMessage("Elapsed time " + sw.Elapsed.ToString() + " Results: " + result);//fitted_params+" "+fitted_param_L2Enorm+" " + fitted_variablevalues);
                //var myresult = result.ToArray();
                
                BroadcastIdentifyResult(result.ToArray(),sw.Elapsed,class1.identify_getssq(),class1.identify_getcomputationcycles());

            }
            catch (Exception e)
            {
                //TODO handle matlab exception
                BroadcastIdentifyStateMessage(e.Message+e.StackTrace);
                logger.ErrorException("exception during identification:",e);
            }
//            StopIdentify();
            FinalizeIdentify();
            
        }

        //result will contain ssq at last position
        private void BroadcastIdentifyResult(object result, TimeSpan elapsed, object ssq, object compcycles)
        {
            var myresult = new List<double>();
            foreach (var o in (Double[,])result) myresult.Add(o);
            //var ssq = myresult.Last();
            //foreach (var o in )
            //double myssq = 0;

            var myssq = ((MWArray) ssq).ToArray();//.GetValue(0));
            var mycompcycles = ((MWArray) compcycles).ToArray(); 
                //myssq = ssq;
            //var myssq = ((MWIndexArray) o).
            //TODO should it be implemented in MATLAB class instead of directly in here?
            //get workers url
            var myrepository = ((AppHostBase)EndpointHost.AppHost).Container.Resolve<WorkersRepository>();
            //var workers = (List<Worker>) myrepository.GetByModelName(identify.model);
            var workers = (List<Worker>)myrepository.GetByModelName(modelname);
            //compute on the first worker (expected that first worker is in localhost)
            var simulator = new GenericSimulator(modelname,masterserviceurl);
            var timepoints = new List<double>();
            foreach (var row in variable_values) timepoints.Add(row[0]); //add first number - time from each experiment value
            
         
                        var responseDto = new ResultDTO()
                                  {
                                      Ssq = (double)myssq.GetValue(0,0), //TODO debug
                                      countcycles = (long)mycompcycles.GetValue(0, 0), //TODO debug
                                      elapsedtime = elapsed.ToString(),
                                      name= "Result of "+modelname+" at "+DateTime.Now,
                                      model = modelname,
                                      Parameternames = parameters.Keys.ToArray(),
                                      Parametervalues = myresult.ToArray(), 
                                      ParameterAssignment = parameters.Values.ToArray(),
                                      Variablenames = variable_names,
                                      Variablevalues = simulator.Simulate(workers.Select(w => w.RestUrl).First(),parameters.Keys.ToArray(),myresult.ToArray(),variable_names.ToArray(),timepoints.ToArray()), //expected that in variable-values[0] is timepoints
                                      Experimentalvalues = variable_values
                                  };
            //Clients.All.closeIdentifyProcess(responseDto);
            var repositoryResult = ((AppHostBase)EndpointHost.AppHost).Container.Resolve<ResultRepository>();
            repositoryResult.Store(responseDto);
            Clients.All.closeIdentifyProcessandResultUpdate(responseDto,repositoryResult.GetAllResultsMeta());
        }


        public void BroadcastIdentifyStateMessage(string message)
        {
            lock (_updateIdentifyState)
            {
                if (!_updatingIdentifyState)
                {
                    _updatingIdentifyState = true;

                    Clients.All.currentMessage(message); //calls method on clients - Javascript, or .NET
                }
                _updatingIdentifyState = false;
            }
        }


        private void BroadcastIdentifyStateChange(IdentifyState running)
        {
            //throw new NotImplementedException();
            Clients.All.updateIdentifyState(IdentifyState);
        }

        private void UpdateIdentifyState(object state)
        {
            lock (_updateIdentifyState)
            {
                if (!_updatingIdentifyState)
                {
                    _updatingIdentifyState = true;
                    
                    Clients.All.currentParams(parameters.Values.ToArray()); //calls method on clients - Javascript, or .NET
                }
                _updatingIdentifyState = false;
            }
        }
        private void fakeUpdateParameter(string key,double value)
        {
            parameters[key].Value = value * (1 + (random.NextDouble() - 0.5) / 10); //update+- 0-10% of it's value
        }

        

        public void StopIdentify()
        {
            lock (_identifyStateLock)
            {
                if (IdentifyState == IdentifyState.Running)
                {
                    computationThread.Abort();
                    IdentifyState = IdentifyState.Stopped;
                    BroadcastIdentifyStateChange(IdentifyState);
                }
            }
        }

        public void FinalizeIdentify()
        {
            lock (_identifyStateLock)
            {
                if (IdentifyState == IdentifyState.Running)
                {
                    IdentifyState = IdentifyState.Stopped;
                    BroadcastIdentifyStateChange(IdentifyState);
                }
            }
        }

        public void SetParameters(string[][] initialparameters)
        {
            lock(_updateIdentifyState)
            {
                parameters.Clear();
                foreach (var row in initialparameters)
                {
                    // remove the key if exists, because on second execution, parameters.Add throws exception that the key already exists
                    if (parameters.ContainsKey(row[0])) parameters.Remove(row[0]);
                    parameters.Add(row[0], new IdentifyParameters(row));
                }
                
            }
         //   throw new NotImplementedException();
        }

        public void SetParameters(IdentifyParameters[] initialparameters)
        {
            lock (_updateIdentifyState)
            {
                foreach (var row in initialparameters)
                {
                    parameters.Add(row.Name,row);
                }

            }
        }

        public void SetExperimentVariables(string[] variablenames)
        {
            lock (_updateIdentifyState)
            {
                //fix bug of "Time    " not found in simulator, trim whitespaces
                variable_names = new string[variablenames.Length]; 
                for (int i = 0; i < variablenames.Length;i++ ) variable_names[i] = variablenames[i].Trim();
                //variable_names = new ConcurrentQueue<string>(variablenames);
            }
        }

        public void SetExperimentValues(double[][] experimentData)
        {
            lock (_updateIdentifyState)
            {
                variable_values = experimentData;
            }
        }

        public void SetModelToIdentify(string name)
        {
            lock (_updateIdentifyState)
            {
                modelname = name;
            }
        }

        public string GetModelToIdentify()
        {
            return modelname;
        }

        public void SetUri(int port)
        {
            if (port == 80) masterserviceurl = "http://localhost/identifikace/";//make configurable - hardcoded to physiome.lf1.cuni.cz
            else if (port > 0) masterserviceurl = "http://localhost:"+port+"/";//make configurable - hardcoded to physiome.lf1.cuni.cz

        }

        public void SetComputationParams(double[] computationParams)
        { //TODO use some DTO
            if (computationParams.Length>0) IAgenerations = computationParams[0];
            if (computationParams.Length > 1) IApopulationsize = computationParams[1];
            if (computationParams.Length > 2) IAtolfun = computationParams[2];
        }
    }
    public enum IdentifyState
    {
        Initial,
        Running,
        Stopped,
        Finished,
    }

}