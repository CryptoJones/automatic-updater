﻿using System;
using System.IO;
using System.Windows.Forms;
using wyUpdate.Common;

namespace wyDay.Controls
{
    /// <summary>
    /// Backend for the AutomaticUpdater control.
    /// </summary>
    public class AutomaticUpdaterBackend
    {
        AutoUpdaterInfo AutoUpdaterInfo;

        readonly UpdateHelper updateHelper = new UpdateHelper();

        UpdateStepOn m_UpdateStepOn;

        UpdateType internalUpdateType = UpdateType.Automatic;
        UpdateType m_UpdateType = UpdateType.Automatic;


        // changes
        string version, changes;
        bool changesAreRTF;

        bool RestartInfoSent;

        #region Events

        /// <summary>
        /// Event is raised before the update checking begins.
        /// </summary>
        public event BeforeHandler BeforeChecking;

        /// <summary>
        /// Event is raised before the downloading of the update begins.
        /// </summary>
        public event BeforeHandler BeforeDownloading;

        /// <summary>
        /// Event is raised when checking or updating is cancelled.
        /// </summary>
        public event EventHandler Cancelled;

        /// <summary>
        /// Event is raised when the checking for updates fails.
        /// </summary>
        public event FailHandler CheckingFailed;

        /// <summary>
        /// Event is raised when an update can't be installed and the closing is aborted.
        /// </summary>
        public event EventHandler ClosingAborted;

        /// <summary>
        /// Event is raised when the update fails to download or extract.
        /// </summary>
        public event FailHandler DownloadingOrExtractingFailed;

        /// <summary>
        /// Event is raised when the current update step progress changes.
        /// </summary>
        public event UpdateProgressChanged ProgressChanged;

        /// <summary>
        /// Event is raised when the update is ready to be installed.
        /// </summary>
        public event EventHandler ReadyToBeInstalled;

        /// <summary>
        /// Event is raised when a new update is found.
        /// </summary>
        public event EventHandler UpdateAvailable;

        /// <summary>
        /// Event is raised when an update fails to install.
        /// </summary>
        public event FailHandler UpdateFailed;

        /// <summary>
        /// Event is raised when an update installs successfully.
        /// </summary>
        public event SuccessHandler UpdateSuccessful;

        /// <summary>
        /// Event is raised when the latest version is already installed.
        /// </summary>
        public event SuccessHandler UpToDate;

        #endregion Events

        #region Properties

        /// <summary>
        /// Gets or sets the arguments to pass to your app when it's being restarted after an update.
        /// </summary>
        public string Arguments { get; set; }

        /// <summary>
        /// Gets the changes for the new update.
        /// </summary>
        public string Changes
        {
            get
            {
                if (!changesAreRTF)
                    return changes;

                // convert the RTF text to plaintext
                using (RichTextBox r = new RichTextBox {Rtf = changes})
                {
                    return r.Text;
                }
            }
        }

        /// <summary>
        /// Gets if this AutomaticUpdater has hidden this form and preparing to install an update.
        /// </summary>
        public bool ClosingForInstall { get; private set; }

        string m_GUID;

        /// <summary>
        /// Gets the GUID (Globally Unique ID) of the automatic updater. It is recommended you set this value (especially if there is more than one exe for your product).
        /// </summary>
        /// <exception cref="System.Exception">Thrown when trying to set the GUID at runtime.</exception>
        public string GUID
        {
            get { return m_GUID; }
            set
            {
                // disallow setting after AutoUpdaterInfo is not null
                if (AutoUpdaterInfo != null)
                    throw new Exception("You must set the GUID at Design time (or before you call the Initialize function).");

                if (value.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
                {
                    // there are bad filename characters
                    throw new Exception("The GUID cannot contain invalid filename characters.");
                }

                m_GUID = value;
            }
        }

        /// <summary>
        /// Gets the date the updates were last checked for.
        /// </summary>
        public DateTime LastCheckDate
        {
            get { return AutoUpdaterInfo.LastCheckedForUpdate; }
        }


        /// <summary>
        /// Gets the update step the AutomaticUpdater is currently on.
        /// </summary>
        public UpdateStepOn UpdateStepOn
        {
            get
            {
                return m_UpdateStepOn;
            }
            private set
            {
                m_UpdateStepOn = value;

                // set the AutoUpdaterInfo property
                if (value != UpdateStepOn.Checking
                    && value != UpdateStepOn.DownloadingUpdate
                    && value != UpdateStepOn.ExtractingUpdate)
                {
                    if (value == UpdateStepOn.Nothing)
                        AutoUpdaterInfo.ClearSuccessError();

                    AutoUpdaterInfo.UpdateStepOn = value;
                    AutoUpdaterInfo.Save();
                }
            }
        }

        /// <summary>
        /// Gets or sets how much this AutomaticUpdater control should do without user interaction.
        /// </summary>
        public UpdateType UpdateType
        {
            get { return m_UpdateType; }
            set
            {
                m_UpdateType = value;
                internalUpdateType = value;
            }
        }

        /// <summary>
        /// Gets the version of the new update.
        /// </summary>
        public string Version
        {
            get
            {
                return version;
            }
        }

        /// <summary>
        /// Gets or sets the arguments to pass to wyUpdate when it is started to check for updates.
        /// </summary>
        public string wyUpdateCommandline
        {
            get { return updateHelper.ExtraArguments; }
            set { updateHelper.ExtraArguments = value; }
        }

        /// <summary>
        /// Gets or sets the relative path to the wyUpdate (e.g. wyUpdate.exe  or  SubDir\\wyUpdate.exe)
        /// </summary>
        public string wyUpdateLocation
        {
            get { return updateHelper.wyUpdateLocation; }
            set { updateHelper.wyUpdateLocation = value; }
        }

        #endregion

        //Methods and such

        /// <summary>
        /// Represents an AutomaticUpdater control.
        /// </summary>
        public AutomaticUpdaterBackend()
        {
            Application.ApplicationExit += Application_ApplicationExit;

            updateHelper.ProgressChanged += updateHelper_ProgressChanged;
            updateHelper.PipeServerDisconnected += updateHelper_PipeServerDisconnected;
            updateHelper.UpdateStepMismatch += updateHelper_UpdateStepMismatch;
        }

        /// <summary>
        /// Proceed with the download and installation of pending updates.
        /// </summary>
        public void InstallNow()
        {
            // throw an exception when trying to Install when no update is ready

            if (UpdateStepOn == UpdateStepOn.Nothing)
                throw new Exception("There must be an update available before you can install it.");
            
            if (UpdateStepOn == UpdateStepOn.Checking)
                throw new Exception(
                    "The AutomaticUpdater must finish checking for updates before they can be installed.");

            if (UpdateStepOn == UpdateStepOn.DownloadingUpdate)
                throw new Exception("The update must be downloaded before you can install it.");

            if (UpdateStepOn == UpdateStepOn.ExtractingUpdate)
                throw new Exception("The update must finish extracting before you can install it.");

            // set the internal update type to autmatic so the user won't be prompted anymore
            internalUpdateType = UpdateType.Automatic;

            if (UpdateStepOn == UpdateStepOn.UpdateAvailable)
            {
                // begin downloading the update
                DownloadUpdate();
            }
            else if (UpdateStepOn == UpdateStepOn.UpdateDownloaded)
            {
                ExtractUpdate();
            }
            else // UpdateReadyToInstall
            {
                // begin installing the update
                InstallPendingUpdate();
            }
        }

        /// <summary>
        /// Cancel the checking, downloading, or extracting currently in progress.
        /// </summary>
        public void Cancel()
        {
            updateHelper.Cancel();

            SetLastSuccessfulStep();

            if (Cancelled != null)
                Cancelled(this, EventArgs.Empty);
        }

        void SetLastSuccessfulStep()
        {
            if (UpdateStepOn == UpdateStepOn.Checking)
                UpdateStepOn = UpdateStepOn.Nothing;
            else
                UpdateStepOn = UpdateStepOn.UpdateAvailable;
        }

        /// <summary>
        /// Check for updates forcefully.
        /// </summary>
        /// <param name="recheck">Recheck with the servers regardless of cached updates, etc.</param>
        /// <returns>Returns true if checking has begun, false otherwise.</returns>
        public bool ForceCheckForUpdate(bool recheck)
        {
            // if not already checking for updates then begin checking.
            if (recheck || UpdateStepOn == UpdateStepOn.Nothing)
            {
                BeforeArgs bArgs = new BeforeArgs();

                if (BeforeChecking != null)
                    BeforeChecking(this, bArgs);

                if (bArgs.Cancel)
                {
                    // close wyUpdate
                    updateHelper.Cancel();
                    return false;
                }

                // show the working animation
                SetUpdateStepOn(UpdateStepOn.Checking);

                if (recheck)
                    updateHelper.ForceRecheckForUpdate();
                else
                    updateHelper.CheckForUpdate();

                return true;
            }

            return false;
        }

        /// <summary>
        /// Check for updates forcefully.
        /// </summary>
        /// <returns>Returns true if checking has begun, false otherwise.</returns>
        public bool ForceCheckForUpdate()
        {
            return ForceCheckForUpdate(false);
        }

        void InstallPendingUpdate()
        {
            // send the client the arguments that need to run on success and failure
            updateHelper.RestartInfo(Application.ExecutablePath, AutoUpdaterInfo.AutoUpdateID, Arguments);
        }

        void DownloadUpdate()
        {
            BeforeArgs bArgs = new BeforeArgs();

            if (BeforeDownloading != null)
                BeforeDownloading(this, bArgs);

            if (bArgs.Cancel)
            {
                // close wyUpdate
                updateHelper.Cancel();
                return;
            }

            // if the control is hidden show it now (so the user can cancel the downloading if they want)
            // show the 'working' animation
            SetUpdateStepOn(UpdateStepOn.DownloadingUpdate);

            updateHelper.DownloadUpdate();
        }

        void ExtractUpdate()
        {
            SetUpdateStepOn(UpdateStepOn.ExtractingUpdate);

            // extract the update
            updateHelper.BeginExtraction();
        }

        void updateHelper_UpdateStepMismatch(object sender, Response respType, UpdateStep previousStep)
        {
            // we can't install right now
            if (previousStep == UpdateStep.RestartInfo)
            {
                if (ClosingAborted != null)
                    ClosingAborted(this, EventArgs.Empty);
            }

            if (respType == Response.Progress)
            {
                switch (updateHelper.UpdateStep)
                {
                    case UpdateStep.CheckForUpdate:
                        SetUpdateStepOn(UpdateStepOn.Checking);
                        break;
                    case UpdateStep.DownloadUpdate:
                        SetUpdateStepOn(UpdateStepOn.DownloadingUpdate);
                        break;
                    case UpdateStep.BeginExtraction:
                        SetUpdateStepOn(UpdateStepOn.ExtractingUpdate);
                        break;
                }
            }
        }

        void updateHelper_PipeServerDisconnected(object sender, EventArgs e)
        {
            // the client should only ever exit after success or failure
            // otherwise it is a premature exit (and needs to be treated as an error)
            if (UpdateStepOn == UpdateStepOn.Checking
                || UpdateStepOn == UpdateStepOn.DownloadingUpdate
                || UpdateStepOn == UpdateStepOn.ExtractingUpdate)
            {
                // wyUpdate premature exit error
                UpdateStepFailed(UpdateStepOn, new FailArgs {wyUpdatePrematureExit = true});
            }
        }

        void updateHelper_ProgressChanged(object sender, UpdateHelperData e)
        {
            switch (e.ResponseType)
            {
                case Response.Failed:

                    // show the error icon & menu
                    // and set last successful step
                    UpdateStepFailed(UpdateStepToUpdateStepOn(e.UpdateStep), new FailArgs { ErrorTitle = e.ExtraData[0], ErrorMessage = e.ExtraData[1] });
                    
                    break;
                case Response.Succeeded:


                    switch (e.UpdateStep)
                    {
                        case UpdateStep.CheckForUpdate:

                            AutoUpdaterInfo.LastCheckedForUpdate = DateTime.Now;

                            // there's an update available
                            if (e.ExtraData.Count != 0)
                            {
                                version = e.ExtraData[0];
                                changes = e.ExtraData[1];
                                changesAreRTF = e.ExtraDataIsRTF[1];

                                // save the changes to the AutoUpdateInfo file
                                AutoUpdaterInfo.UpdateVersion = version;
                                AutoUpdaterInfo.ChangesInLatestVersion = changes;
                                AutoUpdaterInfo.ChangesIsRTF = changesAreRTF;
                            }
                            else
                            {
                                // Clear saved version details for cases where we're
                                // continuing an update (the version details filled
                                // in from the AutoUpdaterInfo file) however,
                                // wyUpdate reports your app has since been updated.
                                // Thus we need to clear the saved info.
                                version = null;
                                changes = null;
                                changesAreRTF = false;

                                AutoUpdaterInfo.ClearSuccessError();
                            }

                            break;
                        case UpdateStep.DownloadUpdate:

                            UpdateStepOn = UpdateStepOn.UpdateDownloaded;

                            break;
                        case UpdateStep.RestartInfo:

                            RestartInfoSent = true;

                            // close this application so it can be updated
                            Application.Exit();

                            break;
                    }

                    StartNextStep(e.UpdateStep);

                    break;
                case Response.Progress:

                    // call the progress changed event
                    if (ProgressChanged != null)
                        ProgressChanged(this, e.Progress);

                    break;
            }
        }

        void Application_ApplicationExit(object sender, EventArgs e)
        {
            if (RestartInfoSent)
            {
                // show client & send the "begin update" message
                updateHelper.InstallNow();
            }
        }

        void StartNextStep(UpdateStep updateStepOn)
        {
            // begin the next step
            switch (updateStepOn)
            {
                case UpdateStep.CheckForUpdate:

                    if (!string.IsNullOrEmpty(version))
                    {
                        // there's an update available

                        if (internalUpdateType == UpdateType.CheckAndDownload
                            || internalUpdateType == UpdateType.Automatic)
                        {
                            UpdateStepOn = UpdateStepOn.UpdateAvailable;

                            // begin downloading the update
                            DownloadUpdate();
                        }
                        else
                        {
                            // show the update ready mark
                            UpdateReady();
                        }
                    }
                    else //no update
                    {
                        // tell the user they're using the latest version
                        AlreadyUpToDate();
                    }

                    break;
                case UpdateStep.DownloadUpdate:

                    // begin extraction
                    if (internalUpdateType == UpdateType.Automatic)
                        ExtractUpdate();
                    else
                        UpdateReadyToExtract();

                    break;
                case UpdateStep.BeginExtraction:

                    // inform the user that the update is ready to be installed
                    UpdateReadyToInstall();

                    break;
            }
        }


        void UpdateReady()
        {
            SetUpdateStepOn(UpdateStepOn.UpdateAvailable);

            if (UpdateAvailable != null)
                UpdateAvailable(this, EventArgs.Empty);
        }

        void UpdateReadyToExtract()
        {
            SetUpdateStepOn(UpdateStepOn.UpdateDownloaded);

            if (ReadyToBeInstalled != null)
                ReadyToBeInstalled(this, EventArgs.Empty);
        }

        void UpdateReadyToInstall()
        {
            SetUpdateStepOn(UpdateStepOn.UpdateReadyToInstall);

            if (ReadyToBeInstalled != null)
                ReadyToBeInstalled(this, EventArgs.Empty);
        }

        void AlreadyUpToDate()
        {
            UpdateStepOn = UpdateStepOn.Nothing;

            if (UpToDate != null)
                UpToDate(this, new SuccessArgs { Version = version });
        }

        void UpdateStepFailed(UpdateStepOn us, FailArgs args)
        {
            SetLastSuccessfulStep();

            switch (us)
            {
                case UpdateStepOn.Checking:

                    if (CheckingFailed != null)
                        CheckingFailed(this, args);

                    break;
                case UpdateStepOn.DownloadingUpdate:

                    if (DownloadingOrExtractingFailed != null)
                        DownloadingOrExtractingFailed(this, args);

                    break;
                case UpdateStepOn.ExtractingUpdate:

                    if (DownloadingOrExtractingFailed != null)
                        DownloadingOrExtractingFailed(this, args);

                    break;
            }
        }

        static UpdateStepOn UpdateStepToUpdateStepOn(UpdateStep us)
        {
            switch(us)
            {
                case UpdateStep.BeginExtraction:
                    return UpdateStepOn.ExtractingUpdate;
                case UpdateStep.CheckForUpdate:
                    return UpdateStepOn.Checking;
                case UpdateStep.DownloadUpdate:
                    return UpdateStepOn.DownloadingUpdate;
                default:
                    throw new Exception("UpdateStep not supported");
            }
        }

        void SetUpdateStepOn(UpdateStepOn uso)
        {
            UpdateStepOn = uso;
        }


        //TODO: if any function is called before Initialize is called, throw an exception

        /// <summary>
        /// The intialize function must be called before you can use any other functions.
        /// </summary>
        public void Initialize()
        {
            // read settings file for last check time
            AutoUpdaterInfo = new AutoUpdaterInfo(m_GUID, null);

            // see if update is pending, if so force install
            if (AutoUpdaterInfo.UpdateStepOn == UpdateStepOn.UpdateReadyToInstall)
            {
                //TODO: test funky non-compliant state file

                // then KillSelf&StartUpdater
                ClosingForInstall = true;

                // start the updater
                InstallPendingUpdate();
            }
        }

        /// <summary>
        /// The function that must be called when your app has loaded.
        /// </summary>
        public void AppLoaded()
        {
            // if we want to kill ourself, then don't bother checking for updates
            if (ClosingForInstall)
                return;

            // get the current update step from the 
            m_UpdateStepOn = AutoUpdaterInfo.UpdateStepOn;

            if (UpdateStepOn != UpdateStepOn.Nothing)
            {
                version = AutoUpdaterInfo.UpdateVersion;
                changes = AutoUpdaterInfo.ChangesInLatestVersion;
                changesAreRTF = AutoUpdaterInfo.ChangesIsRTF;

                switch (UpdateStepOn)
                {
                    case UpdateStepOn.UpdateAvailable:
                        UpdateReady();
                        break;

                    case UpdateStepOn.UpdateReadyToInstall:
                        UpdateReadyToInstall();
                        break;

                    case UpdateStepOn.UpdateDownloaded:

                        // begin extraction
                        if (internalUpdateType == UpdateType.Automatic)
                            ExtractUpdate();
                        else
                            UpdateReadyToExtract();
                        break;
                }
            }
            else
            {
                switch (AutoUpdaterInfo.AutoUpdaterStatus)
                {
                    case AutoUpdaterStatus.UpdateSucceeded:

                        // set the version & changes
                        version = AutoUpdaterInfo.UpdateVersion;
                        changes = AutoUpdaterInfo.ChangesInLatestVersion;
                        changesAreRTF = AutoUpdaterInfo.ChangesIsRTF;

                        if (UpdateSuccessful != null)
                            UpdateSuccessful(this, new SuccessArgs { Version = version });

                        break;
                    case AutoUpdaterStatus.UpdateFailed:

                        if (UpdateFailed != null)
                            UpdateFailed(this, new FailArgs { ErrorTitle = AutoUpdaterInfo.ErrorTitle, ErrorMessage = AutoUpdaterInfo.ErrorMessage });

                        break;
                }

                // clear the changes and resave
                AutoUpdaterInfo.ClearSuccessError();
                AutoUpdaterInfo.Save();
            }
        }
    }
}