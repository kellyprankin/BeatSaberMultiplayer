﻿using BeatSaberMultiplayer.Misc;
using ICSharpCode.SharpZipLib.Zip;
using SimpleJSON;
using SongLoaderPlugin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR;
using VRUI;

namespace BeatSaberMultiplayer
{
    class MultiplayerLobbyViewController : VRUINavigationController
    {
        public string selectedServerIP;
        public int selectedServerPort;

        BSMultiplayerUI ui;

        Button _backButton;

        SongLoader _songLoader;

        MultiplayerLeaderboardViewController _multiplayerLeaderboard;

        GameObject _loadingIndicator;
        private bool isLoading = false;
        public bool _loading { get { return isLoading; } set { isLoading = value; SetLoadingIndicator(isLoading); } }


        TextMeshProUGUI _timerText;
        TextMeshProUGUI _selectText;

        CustomSongInfo _selectedSong;
        int _selectedSongDifficulty;

        SongListTableCell _selectedSongCell;

        SongPreviewPlayer _songPreviewPlayer;

        ServerState _serverState = ServerState.Playing;
        
        float _sendRate = 1f / 20;
        float _sendTimer = 0;

        public PlayerInfo localPlayerInfo;
        string lastPlayerInfo;

        public List<PlayerInfo> _playerInfos = new List<PlayerInfo>();
        List<AvatarController> _avatars = new List<AvatarController>();

        protected override void DidActivate()
        {
            

            ui = BSMultiplayerUI._instance;
            _songLoader = FindObjectOfType<SongLoader>();
            localPlayerInfo = new PlayerInfo(GetUserInfo.GetUserName(), GetUserInfo.GetUserID());

            if (_songPreviewPlayer == null)
            {
                ObjectProvider[] providers = Resources.FindObjectsOfTypeAll<ObjectProvider>().Where(x => x.name == "SongPreviewPlayerProvider").ToArray();

                if (providers.Length > 0)
                {
                    _songPreviewPlayer = providers[0].GetProvidedObject<SongPreviewPlayer>();
                }
            }

            if (_backButton == null)
            {
                _backButton = ui.CreateBackButton(rectTransform);

                _backButton.onClick.AddListener(delegate ()
                {
                    BSMultiplayerClient._instance.DataReceived -= DataReceived;
                    BSMultiplayerClient._instance.DisconnectFromServer();
                    _songPreviewPlayer.CrossfadeToDefault();
                    try
                    {
                        transform.parent.GetComponent<MultiplayerServerHubViewController>().UpdatePage();
                    } catch (Exception e)
                    {
                        Console.WriteLine($"ServerHub exception: {e}");
                    }
                    foreach (AvatarController avatar in _avatars)
                    {
                        Destroy(avatar.gameObject);
                    }
                    DismissModalViewController(null, false);
                    
                });
            }

            if (_timerText == null)
            {
                _timerText = ui.CreateText(rectTransform, "", new Vector2(0f, -5f));
                _timerText.fontSize = 8f;
                _timerText.alignment = TextAlignmentOptions.Center;
                _timerText.rectTransform.sizeDelta = new Vector2(20f, 6f);
            }

            if (_selectText == null)
            {
                _selectText = ui.CreateText(rectTransform, "", new Vector2(0f, -36f));
                _selectText.fontSize = 7f;
                _selectText.alignment = TextAlignmentOptions.Center;
                _selectText.rectTransform.sizeDelta = new Vector2(120f, 6f);
            }

            if (_loadingIndicator == null)
            {
                try
                {
                    _loadingIndicator = ui.CreateLoadingIndicator(rectTransform);
                    (_loadingIndicator.transform as RectTransform).anchorMin = new Vector2(0.5f, 0.5f);
                    (_loadingIndicator.transform as RectTransform).anchorMax = new Vector2(0.5f, 0.5f);
                    (_loadingIndicator.transform as RectTransform).anchoredPosition = new Vector2(0f, 0f);
                    _loadingIndicator.SetActive(true);

                }
                catch (Exception e)
                {
                    Console.WriteLine("EXCEPTION: " + e);
                }
            }

            if(_selectedSongCell == null)
            {
                _selectedSongCell = Instantiate(Resources.FindObjectsOfTypeAll<SongListTableCell>().First(x => x.name == "SongListTableCell"),rectTransform,false);
                
                (_selectedSongCell.transform as RectTransform).anchorMin = new Vector2(0.5f, 0.5f);
                (_selectedSongCell.transform as RectTransform).anchorMax = new Vector2(0.5f, 0.5f);
                (_selectedSongCell.transform as RectTransform).anchoredPosition = new Vector2(-25f, 0);

                _selectedSongCell.gameObject.SetActive(false);
            }
            else
            {
                _selectedSongCell.gameObject.SetActive(false);
            }

            if(_multiplayerLeaderboard == null)
            {
                _multiplayerLeaderboard = ui.CreateViewController<MultiplayerLeaderboardViewController>();
                _multiplayerLeaderboard.rectTransform.anchorMin = new Vector2(0.3f, 0f);
                _multiplayerLeaderboard.rectTransform.anchorMax = new Vector2(0.7f, 1f);

                PushViewController(_multiplayerLeaderboard, true);

            }
            else
            {
                if (_viewControllers.IndexOf(_multiplayerLeaderboard) < 0)
                {
                    PushViewController(_multiplayerLeaderboard, true);
                }

            }

            Console.WriteLine($"Connecting to {selectedServerIP}:{selectedServerPort}");
            if (BSMultiplayerClient._instance.ConnectToServer(selectedServerIP,selectedServerPort))
            {
                BSMultiplayerClient._instance.SendString(JsonUtility.ToJson(new ClientCommand(ClientCommandType.GetServerState)));
                BSMultiplayerClient._instance.SendString(JsonUtility.ToJson(new ClientCommand(ClientCommandType.GetAvailableSongs)));
                StartCoroutine(BSMultiplayerClient._instance.ReceiveFromServerCoroutine());
                BSMultiplayerClient._instance.DataReceived += DataReceived;
            }
            else
            {
                _loading = false;
                TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format("Can't connect to server!"), new Vector2(0f, -48f));
                _errorText.alignment = TextAlignmentOptions.Center;
                Destroy(_errorText.gameObject,5f);
            }

        }

        private void DataReceived(string[] _data)
        {
            if (_data != null && _data.Length > 0)
            {
                foreach (string data in _data)
                {
                    

                    ServerCommand command = null;

                    try
                    {
                        command = JsonUtility.FromJson<ServerCommand>(data);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Can't parse received JSON! Exception: " + e);
                        
                    }

                    try
                    {

                        if (command != null)
                        {
                            switch (command.commandType)
                            {
                                case ServerCommandType.UpdateRequired:
                                    {

                                        _selectText.text = "Plugin version mismatch:\nServer: "+command.version+"\nClient: "+BSMultiplayerClient.version;
                                        BSMultiplayerClient._instance.DisconnectFromServer();
                                        _loading = false;

                                    };break;
                                case ServerCommandType.SetServerState:
                                    {
                                        if (command.serverState == ServerState.Playing)
                                        {
                                            _loading = false;
                                            TimeSpan timeRemaining = TimeSpan.FromSeconds(command.selectedSongDuration - command.selectedSongPlayTime);

                                            _timerText.text = timeRemaining.Minutes.ToString("00") + ":" + timeRemaining.Seconds.ToString("00");

                                            _selectedSong = SongLoader.CustomSongInfos.First(x => x.levelId == command.selectedLevelID);

                                            _selectedSongCell.gameObject.SetActive(true);
                                            (_selectedSongCell.transform as RectTransform).anchoredPosition = new Vector2(14f, 39f);
                                            _selectedSongCell.songName = _selectedSong.songName + "\n<size=80%>" + _selectedSong.songSubName + "</size>";
                                            _selectedSongCell.author = _selectedSong.authorName;


                                            CustomLevelStaticData _songData = SongLoader.CustomLevelStaticDatas.First(x => x.levelId == _selectedSong.levelId);

                                            _selectedSongCell.coverImage = _songData.coverImage;
                                            
                                            _songPreviewPlayer.CrossfadeTo(_songData.previewAudioClip, (float)command.selectedSongPlayTime, (float)(command.selectedSongDuration - command.selectedSongPlayTime - 5f), 1f);
                                            
                                        }
                                        
                                    }; break;
                                case ServerCommandType.SetLobbyTimer:
                                    {
                                        _loading = false;
                                        Console.WriteLine("Set timer text to " + command.lobbyTimer);
                                        _timerText.text = command.lobbyTimer.ToString();
                                        if (_multiplayerLeaderboard != null && _viewControllers.IndexOf(_multiplayerLeaderboard) >= 0)
                                        {
                                            _viewControllers.Remove(_multiplayerLeaderboard);
                                            Destroy(_multiplayerLeaderboard.gameObject);
                                            _songPreviewPlayer.CrossfadeToDefault();
                                            _selectedSongCell.gameObject.SetActive(false);
                                        }
                                    }; break;
                                case ServerCommandType.SetSelectedSong:
                                    {
                                        Console.WriteLine("Set selected song " + command.selectedLevelID);

                                        _loading = false;

                                        if (_selectedSong == null)
                                        {
                                            try
                                            {
                                                _selectedSongDifficulty = command.selectedSongDifficlty;
                                                _selectedSong = SongLoader.CustomSongInfos.First(x => x.levelId == command.selectedLevelID);

                                                _selectText.text = "Next song:";

                                                _selectedSongCell.gameObject.SetActive(true);
                                                (_selectedSongCell.transform as RectTransform).anchoredPosition = new Vector2(-25f, 0);
                                                _selectedSongCell.songName = _selectedSong.songName + "\n<size=80%>" + _selectedSong.songSubName + "</size>";
                                                _selectedSongCell.author = _selectedSong.authorName;


                                                CustomLevelStaticData _songData = SongLoader.CustomLevelStaticDatas.First(x => x.levelId == _selectedSong.levelId);

                                                _selectedSongCell.coverImage = _songData.coverImage;

                                                PlayPreview(_songData);


                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine("EXCEPTION: " + e);
                                            }
                                        }

                                        Console.WriteLine("Done");

                                    }; break;
                                case ServerCommandType.StartSelectedSongLevel:
                                    {
                                        Console.WriteLine("Starting selected song! Song: " + _selectedSong.songName + ", Diff: " + ((LevelStaticData.Difficulty)_selectedSongDifficulty).ToString());

                                        BSMultiplayerClient._instance.DataReceived -= DataReceived;
                                        GameplayOptions gameplayOptions = new GameplayOptions();
                                        gameplayOptions.noEnergy = true;
                                        gameplayOptions.mirror = false;

                                        if (BSMultiplayerClient._instance._mainGameSceneSetupData != null)
                                        {
                                            BSMultiplayerClient._instance._mainGameSceneSetupData.SetData(_selectedSong.levelId, (LevelStaticData.Difficulty)_selectedSongDifficulty, null, null, 0f, gameplayOptions, GameplayMode.SoloStandard, null);
                                            BSMultiplayerClient._instance._mainGameSceneSetupData.TransitionToScene(0.7f);
                                            _selectedSong = null;
                                            return;
                                        }
                                        else
                                        {
                                            Console.WriteLine("SceneSetupData is null!");
                                        }
                                        
                                    }; break;
                                case ServerCommandType.DownloadSongs: {
                                        if (!AllSongsDownloaded(command.songsToDownload))
                                        {
                                            StartCoroutine(DownloadSongs(command.songsToDownload));
                                            BSMultiplayerClient._instance.DisconnectFromServer();
                                        }
                                    };break;
                                case ServerCommandType.SetPlayerInfos: {

                                        if (command.serverState == ServerState.Playing)
                                        {
                                            if (_multiplayerLeaderboard != null)
                                            {

                                                TimeSpan timeRemaining = TimeSpan.FromSeconds(command.selectedSongDuration - command.selectedSongPlayTime);

                                                _timerText.text = timeRemaining.Minutes.ToString("00") + ":" + timeRemaining.Seconds.ToString("00");
                                                try
                                                {
                                                    _multiplayerLeaderboard.SetLeaderboard(command.playerInfos.Select(x => JsonUtility.FromJson<PlayerInfo>(x)).ToArray());


                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine("Leaderboard exception: " + e);
                                                }
                                            }
                                        }
                                        else
                                        {





                                            _playerInfos.Clear();
                                            foreach (string playerStr in command.playerInfos)
                                            {
                                                PlayerInfo player = JsonUtility.FromJson<PlayerInfo>(playerStr);
                                                if (!String.IsNullOrEmpty(player.playerAvatar))
                                                {
                                                    byte[] avatar = Convert.FromBase64String(player.playerAvatar);

                                                    player.rightHandPos = Serialization.ToVector3(avatar.Take(12).ToArray());
                                                    player.leftHandPos = Serialization.ToVector3(avatar.Skip(12).Take(12).ToArray());
                                                    player.headPos = Serialization.ToVector3(avatar.Skip(24).Take(12).ToArray());

                                                    player.rightHandRot = Serialization.ToQuaternion(avatar.Skip(36).Take(16).ToArray());
                                                    player.leftHandRot = Serialization.ToQuaternion(avatar.Skip(52).Take(16).ToArray());
                                                    player.headRot = Serialization.ToQuaternion(avatar.Skip(68).Take(16).ToArray());

                                                }
                                                _playerInfos.Add(player);
                                            }

                                            try
                                            {
                                                if (_avatars.Count > _playerInfos.Count)
                                                {
                                                    List<AvatarController> avatarsToRemove = new List<AvatarController>();
                                                    for (int i = _playerInfos.Count; i < _avatars.Count; i++)
                                                    {
                                                        avatarsToRemove.Add(_avatars[i]);
                                                    }
                                                    foreach (AvatarController avatar in avatarsToRemove)
                                                    {
                                                        _avatars.Remove(avatar);
                                                        Destroy(avatar.gameObject);
                                                    }

                                                }
                                                else if (_avatars.Count < _playerInfos.Count)
                                                {
                                                    for (int i = 0; i < (_playerInfos.Count - _avatars.Count); i++)
                                                    {
                                                        _avatars.Add(new GameObject("Avatar").AddComponent<AvatarController>());

                                                    }
                                                }
                                                
                                                for (int i = 0; i < _playerInfos.Count; i++)
                                                {
                                                    _avatars[i].SetPlayerInfo(_playerInfos[i], 0f, localPlayerInfo.Equals(_playerInfos[i]));
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine($"AVATARS EXCEPTION: {e}");
                                            }




                                        }

                                    };break;


                            }
                            _serverState = command.serverState;
                        }
                        else
                        {
                            Console.WriteLine("Server Command is null!");
                        }
                    }catch(Exception e)
                    {
                        Console.WriteLine("Exception (parse switch): "+e);
                    }
                    

                }
            }
                

            StartCoroutine(BSMultiplayerClient._instance.ReceiveFromServerCoroutine());
        }

        void PlayPreview(LevelStaticData _songData)
        {
            Console.WriteLine("Playing preview for " + _songData.songName);
            if (_songData.previewAudioClip != null)
            {
                if (_songPreviewPlayer != null && _songData != null)
                {
                    try
                    {
                        _songPreviewPlayer.CrossfadeTo(_songData.previewAudioClip, _songData.previewStartTime, _songData.previewDuration, 1f);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Can't play preview! Exception: " + e);
                    }
                }
            }
        }

        IEnumerator DownloadSongByLevelId(string levelId)
        {
            Console.WriteLine("Donwloading "+ levelId.Substring(0, levelId.IndexOf('∎')));

            _selectText.text = "Connecting to BeatSaver.com...";

            UnityWebRequest wwwId = UnityWebRequest.Get("https://beatsaver.com/api.php?mode=hashinfo&hash=" + levelId.Substring(0,levelId.IndexOf('∎')));
            wwwId.timeout = 10;

            yield return wwwId.SendWebRequest();
            

            if (wwwId.isNetworkError || wwwId.isHttpError)
            {
                Console.WriteLine(wwwId.error);

                _selectText.text = "";

                TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format(wwwId.error), new Vector2(0f, -48f));
                _errorText.alignment = TextAlignmentOptions.Center;
                Destroy(_errorText.gameObject, 2f);
            }
            else
            {
                string parse = wwwId.downloadHandler.text;

                JSONNode node = JSON.Parse(parse);
                
                Song _tempSong = new Song(node[0]);

                UnityWebRequest wwwDl = UnityWebRequest.Get("https://beatsaver.com/dl.php?id=" + (_tempSong.id));
                wwwDl.timeout = 10;
                _selectText.text = "Downloading "+HTML5Decode.HtmlDecode(_tempSong.beatname)+"...";
                yield return wwwDl.SendWebRequest();

                if (wwwId.isNetworkError || wwwId.isHttpError)
                {
                    Console.WriteLine(wwwId.error);
                    TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format(wwwId.error), new Vector2(0f, -48f));
                    _errorText.alignment = TextAlignmentOptions.Center;
                    Destroy(_errorText.gameObject, 2f);
                }
                else
                {
                    string zipPath = "";
                    string docPath = "";
                    string customSongsPath = "";
                    try
                    {
                        byte[] data = wwwDl.downloadHandler.data;

                        docPath = Application.dataPath;
                        docPath = docPath.Substring(0, docPath.Length - 5);
                        docPath = docPath.Substring(0, docPath.LastIndexOf("/"));
                        customSongsPath = docPath + "/CustomSongs/";
                        zipPath = customSongsPath + _tempSong.beatname + ".zip";
                        File.WriteAllBytes(zipPath, data);
                        Console.WriteLine("Downloaded zip file!");

                        FastZip zip = new FastZip();

                        Console.WriteLine("Extracting...");
                        zip.ExtractZip(zipPath, customSongsPath, null);

                        File.Delete(zipPath);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("EXCEPTION: " + e);
                        yield break;
                    }


                }



            }
        }

        IEnumerator DownloadSongs(string[] _levelIds)
        {
            _timerText.text = "";
            bool needToRefresh = false;

            foreach(string levelId in _levelIds)
            {
                if(SongLoader.CustomSongInfos.FirstOrDefault(x => x.levelId == levelId) == null)
                {
                    needToRefresh = true;
                    Console.WriteLine("Need to download "+levelId);

                    yield return DownloadSongByLevelId(levelId);
                }
            }
            _selectText.text = "";
            

            yield return null;

            if (needToRefresh)
            {
                SongLoader.Instance.RefreshSongs();
            }

            if (BSMultiplayerClient._instance.ConnectToServer(selectedServerIP,selectedServerPort))
            {

                BSMultiplayerClient._instance.SendString(JsonUtility.ToJson(new ClientCommand(ClientCommandType.GetServerState)));
                StartCoroutine(BSMultiplayerClient._instance.ReceiveFromServerCoroutine());
            }
            else
            {
                _loading = false;
                TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format("Can't connect to server!"), new Vector2(0f, -48f));
                _errorText.alignment = TextAlignmentOptions.Center;
                Destroy(_errorText.gameObject, 5f);
            }

        }


        bool AllSongsDownloaded(string[] levelIds)
        {
            bool allDownloaded = true;

            foreach (string levelId in levelIds)
            {
                if (!SongLoader.CustomSongInfos.Select(x => x.levelId).Contains(levelId))
                {
                    allDownloaded = false;
                }

            }

            return allDownloaded;
        }


        void Update()
        {
            if (_serverState == ServerState.Lobby)
            {
                _sendTimer += Time.deltaTime;
                if (_sendTimer > _sendRate)
                {
                    _sendTimer = 0;
                    localPlayerInfo.playerAvatar = Convert.ToBase64String(
                        Serialization.Combine(
                            Serialization.ToBytes(InputTracking.GetLocalPosition(XRNode.RightHand)),
                            Serialization.ToBytes(InputTracking.GetLocalPosition(XRNode.LeftHand)),
                            Serialization.ToBytes(InputTracking.GetLocalPosition(XRNode.Head)),
                            Serialization.ToBytes(InputTracking.GetLocalRotation(XRNode.RightHand)),
                            Serialization.ToBytes(InputTracking.GetLocalRotation(XRNode.LeftHand)),
                            Serialization.ToBytes(InputTracking.GetLocalRotation(XRNode.Head))
                       ));

                    string playerInfoString = JsonUtility.ToJson(new ClientCommand(ClientCommandType.SetPlayerInfo, JsonUtility.ToJson(localPlayerInfo)));

                    if (playerInfoString != lastPlayerInfo)
                    {
                        BSMultiplayerClient._instance.SendString(playerInfoString);
                        lastPlayerInfo = playerInfoString;
                    }

                }
            }
        }

        void SetLoadingIndicator(bool loading)
        {
            if (_loadingIndicator)
            {
                _loadingIndicator.SetActive(loading);
            }
        }

    }
}
