//  Copyright 2005-2010 Portland State University, University of Wisconsin
//  Authors:  Arjan de Bruijn

using Landis.Core;
using Landis.SpatialModeling;
using Landis.Library.Succession;
using Landis.Library.InitialCommunities;
using System.Collections.Generic;
using Edu.Wisc.Forest.Flel.Util;
using System;
using Landis.Library;
using System.Linq;
using Landis.Library.Parameters.Species;
 

namespace Landis.Extension.Succession.BiomassPnET
{
     
    public class PlugIn  : Landis.Library.Succession.ExtensionBase 
    {

        private static SortedDictionary<string, Parameter<string>> parameters = new SortedDictionary<string, Parameter<string>>(StringComparer.InvariantCultureIgnoreCase);

        public static bool TryGetParameter(string label, out Parameter<string> parameter)
        {
            parameter = null;
            if (label == null)
            {
                return false;
            }

            if (parameters.ContainsKey(label) == false) return false;

            else
            {
               parameter = parameters[label];
               return true;
            }
        }

        public static Parameter<string> GetParameter(string label)
        {
            if (parameters.ContainsKey(label) == false)
            {
                throw new System.Exception("No value provided for parameter " + label);
            }

            return parameters[label];

        }
        public static Parameter<string> GetParameter(string label, float min, float max)
        {
            if (parameters.ContainsKey(label) == false)
            {
                throw new System.Exception("No value provided for parameter " + label);
            }

            Parameter<string> p = parameters[label];

            foreach (KeyValuePair<string, string> value in p)
            {
                float f;
                if (float.TryParse(value.Value, out f) == false)
                {
                    throw new System.Exception("Unable to parse value " + value.Value + " for parameter " + label +" unexpected format.");
                }
                if (f > max || f < min)
                {
                    throw new System.Exception("Parameter value " + value.Value + " for parameter " + label + " is out of range. [" + min + "," + max + "]");
                }
            }
            return p;
            
        }

        public static int DiscreteUniformRandom(int min, int max)
        {
            ModelCore.ContinuousUniformDistribution.Alpha = min;
            ModelCore.ContinuousUniformDistribution.Beta = max;
            ModelCore.ContinuousUniformDistribution.NextDouble();

            int value = (int)ModelCore.ContinuousUniformDistribution.NextDouble();

            return value;
        }

        public static double ContinuousUniformRandom(double min = 0, double max = 1)
        {
            ModelCore.ContinuousUniformDistribution.Alpha = min;
            ModelCore.ContinuousUniformDistribution.Beta = max;
            ModelCore.ContinuousUniformDistribution.NextDouble();

            double value = ModelCore.ContinuousUniformDistribution.NextDouble();

            return value;
        }
        //static ISiteVar<bool> MyDisturbed = PlugIn.ModelCore.Landscape.NewSiteVar<bool>();


        public void CohortDied(object sender, Landis.Library.BiomassCohorts.DeathEventArgs eventArgs)
        {
            ExtensionType disturbanceType = eventArgs.DisturbanceType;
            if (disturbanceType != null)
            {
                ActiveSite site = eventArgs.Site;

                //MyDisturbed[site] = true;

                if (disturbanceType.IsMemberOf("disturbance:fire"))
                    Reproduction.CheckForPostFireRegen(eventArgs.Cohort, site);
                else
                    Reproduction.CheckForResprouting(eventArgs.Cohort, site);
            }
        }
       

        public static DateTime Date{get; private set;}
        
        public static ICore ModelCore;
        
        private static ISiteVar<SiteCohorts> sitecohorts;
        private static ISiteVar<Landis.Library.Biomass.Pool> WoodyDebris;
        private static ISiteVar<Landis.Library.Biomass.Pool> Litter;
        public static ISiteVar<ushort> WaterMaxGrowingSeason;
        private static ISiteVar<float> SubCanopyParMaxGrowingSeason;

        private static ISiteVar<byte> CanopyLAImax;
        
        private static DateTime StartDate;
        
        private static Dictionary<ActiveSite, string> SiteOutputNames;
        
        public static byte IMAX { get; private set; }

        string PnETDefaultsFolder
        {
            get
            {
                return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Defaults";
            }
        }
        
    

        //---------------------------------------------------------------------

        public PlugIn()
            : base(Names.ExtensionName)
        {
            LocalOutput.PNEToutputsites = Names.PNEToutputsites;
        }

        private static Dictionary<string, Parameter<string>> LoadTable(string label, List<string> RowLabels, List<string> Columnheaders, bool transposed = false)
        {
            string filename = GetParameter(label).Value;
            if (System.IO.File.Exists(filename) == false) throw new System.Exception("File not found " + filename);
            ParameterTableParser parser = new ParameterTableParser(filename, label, RowLabels, Columnheaders, transposed);
            Dictionary<string, Parameter<string>> parameters = Landis.Data.Load<Dictionary<string, Parameter<string>>>(filename, parser);
            return parameters;
        }
        //---------------------------------------------------------------------
        public override void LoadParameters(string InputParameterFile, ICore mCore)
        {
            ModelCore = mCore;

            parameters.Add(Names.ExtensionName, new Parameter<string>(Names.ExtensionName, InputParameterFile));

            //-------------PnET-Succession input files
            Dictionary<string, Parameter<string>> InputParameters = LoadTable(Names.ExtensionName, Names.AllNames, null, true);
            InputParameters.ToList().ForEach(x => parameters.Add(x.Key, x.Value));

            //-------------Species parameters
            List<string> SpeciesNames = PlugIn.ModelCore.Species.ToList().Select(x => x.Name).ToList();
            List<string> SpeciesPars = Species.ParameterNames;
            SpeciesPars.Add(Names.PnETSpeciesParameters);
            Dictionary<string, Parameter<string>> speciesparameters = LoadTable(Names.PnETSpeciesParameters, SpeciesNames, SpeciesPars);
            foreach (string key in speciesparameters.Keys)
            {
                if (parameters.ContainsKey(key)) throw new System.Exception("Parameter " + key + " was provided twice");
            }
            speciesparameters.ToList().ForEach(x => parameters.Add(x.Key, x.Value));

            //-------------Ecoregion parameters
            List<string> EcoregionNames = PlugIn.ModelCore.Ecoregions.ToList().Select(x => x.Name).ToList();
            List<string> EcoregionParameters = Ecoregion.ParameterNames;
            Dictionary<string, Parameter<string>> ecoregionparameters = LoadTable(Names.EcoregionParameters, EcoregionNames, EcoregionParameters);
            foreach (string key in ecoregionparameters.Keys)
            {
                if (parameters.ContainsKey(key)) throw new System.Exception("Parameter "+ key +" was provided twice");
            }

            ecoregionparameters.ToList().ForEach(x => parameters.Add(x.Key, x.Value));

            //---------------AgeOnlyDisturbancesParameterFile
            Parameter<string> AgeOnlyDisturbancesParameterFile;
            if (TryGetParameter(Names.AgeOnlyDisturbances, out AgeOnlyDisturbancesParameterFile))
            {
                List<string> DisturbanceTypes = Allocation.Disturbances.AllNames;
                List<string> DisturbanceAllocations = Allocation.Reductions.AllNames;
                Dictionary<string, Parameter<string>> AgeOnlyDisturbancesParameters = LoadTable(Names.AgeOnlyDisturbances, DisturbanceAllocations, DisturbanceTypes);
                foreach (string key in AgeOnlyDisturbancesParameters.Keys)
                {
                    if (parameters.ContainsKey(key)) throw new System.Exception("Parameter " + key + " was provided twice");
                }
                AgeOnlyDisturbancesParameters.ToList().ForEach(x => parameters.Add(x.Key, x.Value));
            }

            //---------------VanGenughtenParameterFile
            if (parameters.ContainsKey(PressureHeadVanGenuchten.VanGenuchtenParameters) == false)
            {
                Parameter<string> VanGenuchtenParameterFile = new Parameter<string>(PressureHeadVanGenuchten.VanGenuchtenParameters, (string)PnETDefaultsFolder + "\\VanGenuchtenParameters.txt");
                parameters.Add(PressureHeadVanGenuchten.VanGenuchtenParameters, VanGenuchtenParameterFile); 
            }
            Dictionary<string, Parameter<string>> VanGenughtenParameters = LoadTable(PressureHeadVanGenuchten.VanGenuchtenParameters, null, PressureHeadVanGenuchten.ParameterNames);
            foreach (string key in VanGenughtenParameters.Keys)
            {
                if (parameters.ContainsKey(key)) throw new System.Exception("Parameter " + key + " was provided twice");
            }
            VanGenughtenParameters.ToList().ForEach(x => parameters.Add(x.Key, x.Value));

            //---------------SaxtonAndRawlsParameterFile
            if (parameters.ContainsKey(PressureHeadSaxton_Rawls.SaxtonAndRawlsParameters) == false)
            {
                Parameter<string> SaxtonAndRawlsParameterFile = new Parameter<string>(PressureHeadSaxton_Rawls.SaxtonAndRawlsParameters, (string)PnETDefaultsFolder + "\\SaxtonAndRawlsParameters.txt");
                parameters.Add(PressureHeadSaxton_Rawls.SaxtonAndRawlsParameters, SaxtonAndRawlsParameterFile);
            }
            Dictionary<string, Parameter<string>> SaxtonAndRawlsParameters = LoadTable(PressureHeadSaxton_Rawls.SaxtonAndRawlsParameters, null, PressureHeadSaxton_Rawls.ParameterNames);
            foreach (string key in SaxtonAndRawlsParameters.Keys)
            {
                if (parameters.ContainsKey(key)) throw new System.Exception("Parameter " + key + " was provided twice");
            }
            SaxtonAndRawlsParameters.ToList().ForEach(x => parameters.Add(x.Key, x.Value));

            //--------------PnETGenericParameterFile

            //----------See if user supplied overwriting default parameters
            List<string> RowLabels = new List<string>(Names.AllNames);
            RowLabels.AddRange(Species.ParameterNames); 

            if (parameters.ContainsKey(Names.PnETGenericParameters))
            {
                Dictionary<string, Parameter<string>> genericparameters = LoadTable(Names.PnETGenericParameters,  RowLabels, null, true);
                foreach (KeyValuePair<string, Parameter<string>> par in genericparameters)
                {
                    if (parameters.ContainsKey(par.Key)) throw new System.Exception("Parameter " + par.Key + " was provided twice");
                    parameters.Add(par.Key, par.Value);
                }
            }

            //----------Load in default parameters to fill the gaps
            Parameter<string> PnETGenericDefaultParameterFile = new Parameter<string>(Names.PnETGenericDefaultParameters, (string)PnETDefaultsFolder + "\\PnETGenericDefaultParameters.txt");
            parameters.Add(Names.PnETGenericDefaultParameters, PnETGenericDefaultParameterFile);
            Dictionary<string, Parameter<string>> genericdefaultparameters = LoadTable(Names.PnETGenericDefaultParameters, RowLabels, null, true);

            foreach (KeyValuePair<string, Parameter<string>> par in genericdefaultparameters)
            {
                if (parameters.ContainsKey(par.Key) == false)
                {
                    parameters.Add(par.Key, par.Value);
                }
            }

            SiteOutputNames = new Dictionary<ActiveSite, string>();
            Parameter<string> OutputSitesFile;
            if (TryGetParameter(LocalOutput.PNEToutputsites, out OutputSitesFile))
            {
                Dictionary<string, Parameter<string>> outputfiles = LoadTable(LocalOutput.PNEToutputsites, null, AssignOutputFiles.ParameterNames.AllNames, true);
                AssignOutputFiles.MapCells(outputfiles, ref SiteOutputNames);
            }

        }

        
       
       
       
        
        public override void Initialize()
        {
            PlugIn.ModelCore.UI.WriteLine("Initializing " + Names.ExtensionName + " version " + typeof(PlugIn).Assembly.GetName().Version);

            Cohort.DeathEvent += CohortDied;
             

            sitecohorts = PlugIn.ModelCore.Landscape.NewSiteVar<SiteCohorts>();
            Edu.Wisc.Forest.Flel.Util.Directory.EnsureExists("output");

            Timestep = ((Parameter<int>)GetParameter(Names.Timestep)).Value;

            Species.Initialize();
            Ecoregion.Initialize();
            Hydrology.Initialize();
            SiteCohorts.Initialize();
            EcoregionDateData.Initialize();
            Canopy.Initialize();
            
            IMAX = ((Parameter<byte>)GetParameter(Names.IMAX)).Value;

            // Initialize Reproduction routines:
            Reproduction.SufficientResources = SufficientResources;
            Reproduction.Establish = Establish;
            Reproduction.AddNewCohort = AddNewCohort;
            Reproduction.MaturePresent = MaturePresent;
            Reproduction.PlantingEstablish = PlantingEstablish;

            
            SeedingAlgorithms SeedAlgorithm = (SeedingAlgorithms)Enum.Parse(typeof(SeedingAlgorithms), parameters["SeedingAlgorithm"].Value);
            
            base.Initialize(ModelCore, SeedAlgorithm);
             
             
            StartDate = new DateTime(((Parameter<int>)GetParameter(Names.StartYear)).Value, 1, 15);

            PlugIn.ModelCore.UI.WriteLine("Spinning up biomass");

            WoodyDebris = PlugIn.ModelCore.Landscape.NewSiteVar<Landis.Library.Biomass.Pool>();
            Litter = PlugIn.ModelCore.Landscape.NewSiteVar<Landis.Library.Biomass.Pool>();
            WaterMaxGrowingSeason = PlugIn.ModelCore.Landscape.NewSiteVar<ushort>();

            SubCanopyParMaxGrowingSeason = PlugIn.ModelCore.Landscape.NewSiteVar<float>();
            CanopyLAImax = PlugIn.ModelCore.Landscape.NewSiteVar<byte>();

            string InitialCommunitiesTXTFile = GetParameter(Names.InitialCommunities).Value;
            string InitialCommunitiesMapFile = GetParameter(Names.InitialCommunitiesMap).Value;
            InitializeSites(InitialCommunitiesTXTFile, InitialCommunitiesMapFile, ModelCore);
             
            ISiteVar<Landis.Library.BiomassCohorts.ISiteCohorts> biomassCohorts = PlugIn.ModelCore.Landscape.NewSiteVar<Landis.Library.BiomassCohorts.ISiteCohorts>();
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                biomassCohorts[site] = sitecohorts[site];

                if (sitecohorts[site] != null && biomassCohorts[site] == null)
                {
                    throw new System.Exception("Cannot convert PnET SiteCohorts to biomass site cohorts");
                }
            }
            ModelCore.RegisterSiteVar(biomassCohorts, "Succession.BiomassCohorts");

            ISiteVar<Landis.Library.AgeOnlyCohorts.ISiteCohorts> AgeCohortSiteVar = PlugIn.ModelCore.Landscape.NewSiteVar<Landis.Library.AgeOnlyCohorts.ISiteCohorts>();
              
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                AgeCohortSiteVar[site] = sitecohorts[site];
            }

            ModelCore.RegisterSiteVar(AgeCohortSiteVar, "Succession.AgeCohorts");
            ModelCore.RegisterSiteVar(ListSiteCohorts(), "Succession.CohortsPnET");
            ModelCore.RegisterSiteVar(CanopyLAImax, "Succession.CanopyLAImax");
            ModelCore.RegisterSiteVar(WoodyDebris, "Succession.WoodyDebris");
            ModelCore.RegisterSiteVar(Litter, "Succession.Litter");
            ModelCore.RegisterSiteVar(SubCanopyParMaxGrowingSeason, "Succession.SubCanopyRadiation");
            ModelCore.RegisterSiteVar(WaterMaxGrowingSeason, "Succession.SoilWater");
             
        }
  
          //---------------------------------------------------------------------
        public ISiteVar<List<Cohort>> ListSiteCohorts()
        {
            ISiteVar<List<Cohort>> cohorts_local = PlugIn.ModelCore.Landscape.NewSiteVar<List<Cohort>>();
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                cohorts_local[site] = new List<Cohort>();

                foreach (List<Cohort> species_cohort in sitecohorts[site].cohorts.Values)
                {
                    cohorts_local[site].AddRange(species_cohort);
                }
            }
            return cohorts_local;
        }
         
        public void AddNewCohort(ISpecies species, ActiveSite site)
        {
            Cohort cohort = new Cohort(species, (ushort)Date.Year, IMAX, species.InitialNSC(), species.DNSC());
            
            if(SiteOutputNames.ContainsKey(site))
            {
                cohort.InitializeOutput(SiteOutputNames[site], (ushort)Date.Year, PlugIn.ModelCore.UI.WriteLine);
            }

            sitecohorts[site].AddNewCohort(cohort, Timestep);

        }
        public bool MaturePresent(ISpecies species, ActiveSite site)
        {
            return sitecohorts[site].IsMaturePresent(species);
        }
        public static SiteCohorts GetFromKey(uint InitialCommunityMapCode, ushort EcoregionMapCode, ActiveSite site)
        {
            uint key = SiteCohorts.ComputeKey(InitialCommunityMapCode, EcoregionMapCode);

            SiteCohorts s = null;
            if (SiteCohorts.initialSites.TryGetValue(key, out s))
            {
               return s;
            }
            return null;
        }

        
        

        MyClock m = null;
        protected override void InitializeSite(ActiveSite site,
                                               ICommunity initialCommunity)
        {
            if (m == null)
            {
                m = new MyClock(PlugIn.ModelCore.Landscape.ActiveSiteCount);
            }

            m.Next();
            m.WriteUpdate();

            //float EstimatedTotalTime = (float)(100.0 / Percentage * (sw.ElapsedMilliseconds / 1000.0));
            //Console.Write("\rInitialization progress {0}% Elapsed time {1} Estimated total time {2}   ", System.String.Format("{0:0.00}", Percentage), Math.Round(sw.ElapsedMilliseconds / 1000F, 0), Math.Round(EstimatedTotalTime, 0));


            SiteCohorts identicalsitecohort = GetFromKey(initialCommunity.MapCode, PlugIn.ModelCore.Ecoregion[site].MapCode, site);

            // Create new sitecohorts
            if(SiteOutputNames.ContainsKey(site))
            {
                sitecohorts[site] = new SiteCohorts(StartDate,
                                                   site,
                                                   initialCommunity,
                                                    SiteOutputNames[site]);

                
            }
            else if (identicalsitecohort != null)
            {
                // Copy existing site data
                sitecohorts[site] = new SiteCohorts(identicalsitecohort, site);
            }
            // Create new sitecohorts
            else
            {
                sitecohorts[site] = new SiteCohorts(StartDate,
                                                    site, 
                                                    initialCommunity);

            }
            CanopyLAImax[site] = sitecohorts[site].CanopyLAImax;
            WoodyDebris[site] = sitecohorts[site].WoodyDebris;
            Litter[site] = sitecohorts[site].Litter;
            SubCanopyParMaxGrowingSeason[site] = sitecohorts[site].SubCanopyParMAX;
            WaterMaxGrowingSeason[site] = sitecohorts[site].WaterMAX; 

           
        }
        protected override void AgeCohorts(ActiveSite site,
                                            ushort years,
                                            int? successionTimestep
                                            )
        {
            DateTime date = new DateTime(PlugIn.StartDate.Year + PlugIn.ModelCore.CurrentTime - Timestep, 1, 15);

            DateTime EndDate = date.AddYears(years);
            IEcoregion Ecoregion = PlugIn.ModelCore.Ecoregion[site];
 
            List<EcoregionDateData> data = EcoregionDateData.Get(Ecoregion, date, EndDate);

            
            sitecohorts[site].Grow(data);

            CanopyLAImax[site] = sitecohorts[site].CanopyLAImax;


            SubCanopyParMaxGrowingSeason[site] = sitecohorts[site].SubCanopyParMAX;
            WaterMaxGrowingSeason[site] = sitecohorts[site].WaterMAX; 

            WoodyDebris[site] = sitecohorts[site].WoodyDebris;
            Litter[site] = sitecohorts[site].Litter;

            Date = EndDate;

            data = null;
        }
        //---------------------------------------------------------------------
        
        public override void Run()
        {
            base.Run();
        }
        public override byte ComputeShade(ActiveSite site)
        {
            

            return 0;
        }

        
        public void AddLittersAndCheckResprouting(object sender, Landis.Library.AgeOnlyCohorts.DeathEventArgs eventArgs)
        {
            if (eventArgs.DisturbanceType != null)
            {
                ActiveSite site = eventArgs.Site;
                Disturbed[site] = true;

                if (eventArgs.DisturbanceType.IsMemberOf("disturbance:fire"))
                    Reproduction.CheckForPostFireRegen(eventArgs.Cohort, site);
                else
                    Reproduction.CheckForResprouting(eventArgs.Cohort, site);
            }
            

        }
        
        
        
        
        //---------------------------------------------------------------------
        public bool SufficientResources(ISpecies species, ActiveSite site)
        {
            return true;
        }

        public bool Establish(ISpecies species, ActiveSite site)
        {
            return EstablishmentProbability.ComputeEstablishment(Date, species, sitecohorts[site].Pest[species], sitecohorts[site].establishment_siteoutput);
        }

        //---------------------------------------------------------------------

        
        
        
        //---------------------------------------------------------------------

        /// <summary>
        /// Determines if a species can establish on a site.
        /// This is a Delegate method to base succession.
        /// </summary>
        public bool PlantingEstablish(ISpecies species, ActiveSite site)
        {
            return true;
           
        }

    }
}


