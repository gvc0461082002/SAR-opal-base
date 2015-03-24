﻿using UnityEngine;
using System;
using System.Collections.Generic;
using TouchScript.Gestures;
using TouchScript.Hit;

/**
 * The SAR-opal-base game main controller. Orchestrates everything: 
 * sets up to receive input via ROS, initializes scenes and creates 
 * game objecgs based on that input, deals with touch events and
 * other tablet-specific things.
 */
public class MainGameController : MonoBehaviour
{
    // gesture manager
    private GestureManager gestureManager = null;
    
    // rosbridge websocket client
    private RosbridgeWebSocketClient clientSocket = null;
    
    // actions for main thread, because the network messages from the
    // websocket can come in on another thread
    readonly static Queue<Action> ExecuteOnMainThread = new Queue<Action>();

    /** Called on start, use to initialize stuff  */
    void Start ()
    {
        // find gesture manager
        FindGestureManager(); 
        this.gestureManager.logEvent += new LogEventHandler(HandleLogEvent);
       
        // Create a new game object programmatically as a test
        //PlayObjectProperties pops = new PlayObjectProperties();
        //pops.setAll("ball2", Constants.TAG_PLAY_OBJECT, false, "chimes", 
        //            new Vector3 (-200, 50, -2), null);
        //this.InstantiatePlayObject(pops);
        
        // Create a new background programmatically as a test
        BackgroundObjectProperties bops = new BackgroundObjectProperties();
        bops.setAll("playground", Constants.TAG_BACKGROUND, 
                    new Vector3(0,0,2));
        this.InstantiateBackground(bops);
        
		// set up rosbridge websocket client
		// note: does not attempt to reconnect if connection fails
		if (this.clientSocket == null)
		{
            // load websocket config from file
            string server = "";
            string port = "";
            string path = "";
            
            // find the websocket config file
            #if UNITY_ANDROID
            path = Constants.CONFIG_PATH_ANDROID + Constants.WEBSOCKET_CONFIG;
            Debug.Log("trying android path: " + path);
            #endif
            
            #if UNITY_EDITOR
            path = Application.dataPath + Constants.CONFIG_PATH_OSX + Constants.WEBSOCKET_CONFIG;
            Debug.Log("osx 1 path: " + path);
            #endif
        
            // load file
            if(!RosbridgeUtilities.DecodeWebsocketJSONConfig(path, out server, out port))
            {
                Debug.LogWarning("Could not read websocket config file! Trying "
                                 + "hardcoded IP 18.85.39.32 and port 9090");
                this.clientSocket = new RosbridgeWebSocketClient(
                    "18.85.39.32",// server, // can pass hostname or IP address
                    "9090"); //port);   
            }
            else
            {
                this.clientSocket = new RosbridgeWebSocketClient(
                    server, // can pass hostname or IP address
                    port);  
            }
			
			this.clientSocket.SetupSocket();
			this.clientSocket.receivedMsgEvent += 
				new ReceivedMessageEventHandler(HandleClientSocketReceivedMsgEvent);
				
            // advertise that we will publish opal_tablet messages
			this.clientSocket.SendMessage(RosbridgeUtilities.GetROSJsonAdvertiseMsg(
                Constants.LOG_ROSTOPIC, Constants.LOG_ROSMSG_TYPE));
            
            // advertise that we will publish opal_tablet_action messages
            this.clientSocket.SendMessage(RosbridgeUtilities.GetROSJsonAdvertiseMsg(
                Constants.ACTION_ROSTOPIC, Constants.ACTION_ROSMSG_TYPE));
            
            // subscribe to opal command messages
            this.clientSocket.SendMessage(RosbridgeUtilities.GetROSJsonSubscribeMsg(
                Constants.CMD_ROSTOPIC, Constants.CMD_ROSMSG_TYPE));
                
            // public string message to opal_tablet
            this.clientSocket.SendMessage(RosbridgeUtilities.GetROSJsonPublishStringMsg(
                Constants.LOG_ROSTOPIC, "Opal tablet checking in!"));
		}
    }


    /** On enable, initialize stuff */
    private void OnEnable ()
    {
        
    }

    /** On disable, disable some stuff */
    private void OnDestroy ()
    {
		// close websocket
		if (this.clientSocket != null)
		{
			this.clientSocket.CloseSocket();
    
			// unsubscribe from received message events
			this.clientSocket.receivedMsgEvent -= HandleClientSocketReceivedMsgEvent;
		}
		
		Debug.Log("destroyed main game controller");
    }
    
    /** 
     * Update is called once per frame 
     */
    void Update ()
    {
        // if user presses escape or 'back' button on android, exit program
        if (Input.GetKeyDown (KeyCode.Escape))
            Application.Quit ();
        
        // dispatch stuff on main thread (usually stuff in response to 
        // messages received from the websocket on another thread)
        while (ExecuteOnMainThread.Count > 0)
        {
            Debug.Log("Invoking....");
            ExecuteOnMainThread.Dequeue().Invoke(); 
        }
    }

    /// <summary>
    /// Instantiate a new game object with the specified properties
    /// </summary>
    /// <param name="pops">properties of the play object.</param>
    void InstantiatePlayObject (PlayObjectProperties pops)
    {
        GameObject go = new GameObject ();

        // set object name
        go.name = (pops.Name () != "") ? pops.Name () : UnityEngine.Random.value.ToString ();
        Debug.Log ("Creating new play object: " + pops.Name ());

        // set tag
        go.tag = Constants.TAG_PLAY_OBJECT;

        // move object to initial position 
        go.transform.position = pops.InitPosition();

        // load audio - add an audio source component to the object if there
        // is an audio file to load
        if (pops.AudioFile() != null) {
            AudioSource audioSource = go.AddComponent<AudioSource>();
            try {
                // to load a sound file this way, the sound file needs to be in an existing 
                // Assets/Resources folder or subfolder 
                audioSource.clip = Resources.Load(Constants.AUDIO_FILE_PATH + 
                                                  pops.AudioFile()) as AudioClip;
            } catch (UnityException e) {
                Debug.Log("ERROR could not load audio: " + pops.AudioFile() + "\n" + e);
            }
            audioSource.loop = false;
            audioSource.playOnAwake = false;
        }

        // load sprite/image for object
        SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
        Sprite sprite = Resources.Load<Sprite>(Constants.GRAPHICS_FILE_PATH + pops.Name());
        if (sprite == null)
            Debug.Log ("ERROR could not load sprite: " 
                + Constants.GRAPHICS_FILE_PATH + pops.Name());
        spriteRenderer.sprite = sprite; 

        // TODO should the scale be a parameter too?
        go.transform.localScale = new Vector3 (100, 100, 100);

        // add rigidbody
        Rigidbody2D rb2d = go.AddComponent<Rigidbody2D>();
        rb2d.gravityScale = 0; // don't want gravity, otherwise objects will fall

        // add polygon collider
        go.AddComponent<CircleCollider2D>();

        // add and subscribe to gestures
        if (this.gestureManager == null ) {
            Debug.Log ("ERROR no gesture manager");
            FindGestureManager();
        }
        
        // add gestures and register to get event notifications
        this.gestureManager.AddAndSubscribeToGestures(go, pops.draggable);
        
        // add pulsing behavior (draws attention to actionable objects)
        go.AddComponent<GrowShrinkBehavior>();
        
        // save the initial position in case we need to reset this object later
        go.AddComponent<SavedProperties>();
        go.GetComponent<SavedProperties>().initialPosition = pops.InitPosition();
        
    }
    
    /// <summary>
    /// Instantiates a background image object
    /// </summary>
    /// <param name="bops">properties of the background image object to load</param>
    private void InstantiateBackground(BackgroundObjectProperties bops)
    {
        // remove previous background if there was one
        this.DestroyObjectsByTag(new string[] {Constants.TAG_BACKGROUND});
    
        // now make a new background
        GameObject go = new GameObject();
        
        // set object name
        go.name = (bops.Name() != "") ? bops.Name() : UnityEngine.Random.value.ToString ();
        Debug.Log ("Creating new background: " + bops.Name ());
        
        // set tag
        go.tag = Constants.TAG_BACKGROUND;
        
        // move object to initial position 
        go.transform.position = bops.InitPosition();
        
        // load sprite/image for object
        SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
        Sprite sprite = Resources.Load<Sprite>(Constants.GRAPHICS_FILE_PATH + bops.Name());
        if (sprite == null)
            Debug.Log ("ERROR could not load sprite: " 
                       + Constants.GRAPHICS_FILE_PATH + bops.Name());
        spriteRenderer.sprite = sprite; 
        
        // TODO should the scale be a parameter too?
        go.transform.localScale = new Vector3 (100, 100, 100);
        
        
    }
    
    /** Find the gesture manager */ 
    private void FindGestureManager()
    {
        // find gesture manager
        this.gestureManager = (GestureManager) GameObject.FindGameObjectWithTag(
            Constants.TAG_GESTURE_MAN).GetComponent<GestureManager>();
        if (this.gestureManager == null) {
            Debug.Log("ERROR: Could not find gesture manager!");
        }
        else {
            Debug.Log("Got gesture manager");
        }
    }
    
    /**
     * Received message from remote controller - process and deal with message
     * */
    void HandleClientSocketReceivedMsgEvent (object sender, int cmd, object props)
    {
        Debug.Log ("MSG received from remote: " + cmd);
        this.clientSocket.SendMessage(RosbridgeUtilities.GetROSJsonPublishStringMsg(
            Constants.LOG_ROSTOPIC, "got message"));
        
        // process first token to determine which message type this is
        // if there is a second token, this is the message argument
        switch (cmd)
        {
        case Constants.DISABLE_TOUCH:
            // disable touch events from user
            this.gestureManager.allowTouch = false; 
            break;
            
        case Constants.ENABLE_TOUCH:
            // enable touch events from user
            this.gestureManager.allowTouch = true;
            break;
            
        case Constants.RESET:
            // reload the current level
            // e.g., when the robot's turn starts, want all characters back in their
            // starting configuration for use with automatic playbacks
            MainGameController.ExecuteOnMainThread.Enqueue(() => { 
                this.ReloadScene();
            });
            break;
            
        case Constants.SIDEKICK_DO:
            // trigger animation for sidekick character
            MainGameController.ExecuteOnMainThread.Enqueue(() => { 
                Sidekick.SidekickDo((string)props);
            }); 
            break;
            
        case Constants.SIDEKICK_SAY:
            // trigger playback of speech for sidekick character
            MainGameController.ExecuteOnMainThread.Enqueue(() => { 
                Sidekick.SidekickSay((string)props);
            }); 
            break;
            
        case Constants.LOAD_OBJECT:
            if (props == null) {
                Debug.Log ("was told to load an object, but got no properties!");
                return;
            }
            
            SceneObjectProperties sops = (SceneObjectProperties) props;
            if (props != null)
            {
                // load new background image with the specified properties
                if (sops.Tag().Equals(Constants.TAG_BACKGROUND))
                {                Debug.Log("background");
                    MainGameController.ExecuteOnMainThread.Enqueue(() => { 
                        this.InstantiateBackground((BackgroundObjectProperties) sops);
                    }); 
                }
                // or instantiate new playobject with the specified properties
                else if (sops.Tag().Equals(Constants.TAG_PLAY_OBJECT))
                {
                    Debug.Log("play object");
                    MainGameController.ExecuteOnMainThread.Enqueue(() => { 
                        this.InstantiatePlayObject((PlayObjectProperties) sops);
                    });
                }
            }
            break;
            
        case Constants.CLEAR:
            Debug.LogWarning("Action clear not fully implemented yet, may break!");
            // remove all play objects and background objects from scene, hide highlight
            MainGameController.ExecuteOnMainThread.Enqueue(() => { 
                this.ClearScene(); // ClearScene works fine, but websocket problem:
            });
            // TODO Something pretty weird is going on here - websocket bug?
            // If we execute the exact same code here as in case Constants.Reload, it 
            // works there but not here - we get an exception from the websocket. 
            // Might be a bug in the websocket code: https://github.com/sta/websocket-sharp/issues/41
            break;
            
        case Constants.MOVE_OBJECT:
            Constants.MoveObject mo = (Constants.MoveObject) props;
            if (props != null)
            {
                // use LeanTween to move object from curr_posn to new_posn
                MainGameController.ExecuteOnMainThread.Enqueue(() => { 
                    GameObject go = GameObject.Find(mo.name);
                    if (go != null) LeanTween.move(go, mo.destination, 2.0f).setEase(LeanTweenType.easeOutSine);    
                });
            }
            break;
            
        case Constants.HIGHLIGHT_OBJECT:
            Debug.LogWarning("Action highlight_object not fully tested yet, may break!");
            MainGameController.ExecuteOnMainThread.Enqueue(() => { 
                GameObject go = GameObject.Find((string) props);
                if (go != null) this.gestureManager.LightOn(go.transform.position);
            });  
            break;
            
        case Constants.REQUEST_KEYFRAME:
            Debug.LogWarning("Action request_keyframe not implemented yet!");
            // TODO send back keyframe log message ...
            
            break;
            
        case Constants.GOT_TO_GOAL:
            Debug.LogWarning("Action got_to_goal not implemented yet!");
            // TODO do something now that object X is at its goal ...
            
            break;
        
        
        }
    }
    
    /// <summary>
    /// Reload the current scene by moving all objects back to
    /// their initial positions and resetting any other relevant
    /// things
    /// </summary>
    void ReloadScene()
    {
        Debug.Log("Reloading current scene...");
        
        // turn light off if it's not already
        this.gestureManager.LightOff();

        // move all play objects back to their initial positions
        ResetAllObjectsWithTag(new string[] {Constants.TAG_PLAY_OBJECT});
        
        // TODO is there anything else to reset?
    }
    
    /// <summary>
    /// Clears the scene, deletes all objects
    /// </summary>
    void ClearScene()
    {
        Debug.Log("Clearing current scene...");
        
        // turn off the light if it's not already
        this.gestureManager.LightOff();
        
        // remove all objects with specified tags
        this.DestroyObjectsByTag(new string[] {Constants.TAG_BACKGROUND, Constants.TAG_PLAY_OBJECT});
    }
    
    /// <summary>
    /// Resets all objects with the specified tags back to initial positions
    /// </summary>
    /// <param name="tags">tags of object types to reset</param>
    void ResetAllObjectsWithTag(string[] tags)
    {
        // move objects with the specified tags
        foreach (string tag in tags)
        {
            // find all objects with the specified tag
            GameObject[] objs = GameObject.FindGameObjectsWithTag(tag);
            if (objs.Length == 0) continue;
            foreach (GameObject go in objs)
            {
                Debug.Log ("moving " + go.name);
                // if the initial position was saved, move to it
                SavedProperties spop = go.GetComponent<SavedProperties>();
                if (ReferenceEquals(spop,null))
                {
                    Debug.LogWarning("Tried to reset " + go.name + " but could not find " +
                                     " any saved properties.");
                }
                else
                {
                    go.transform.position = spop.initialPosition;  
                }
            }
        }
    }
    
    /// <summary>
    /// Destroy objects with the specified tags
    /// </summary>
    /// <param name="tags">tags of objects to destroy</param>
    void DestroyObjectsByTag(string[] tags)
    {
        // destroy objects with the specified tags
        foreach (string tag in tags)
        {
            GameObject[] objs = GameObject.FindGameObjectsWithTag(tag);
            if (objs.Length == 0) continue;
            foreach (GameObject go in objs)
            {
                Debug.Log("destroying " + go.name);
                Destroy(go);
            }
        }
    }
    
    /// <summary>
    /// Logs the state of the current scene and sends as a ROS message
    /// </summary>
    private string GetSceneKeyframe()
    {
        // find background image
        GameObject backg = GameObject.FindGameObjectWithTag(Constants.TAG_BACKGROUND);
        
        // find all game objects currently in scene
        GameObject[] gos = GameObject.FindGameObjectsWithTag(Constants.TAG_PLAY_OBJECT);
        Constants.SceneObject[] sos = new Constants.SceneObject[gos.Length];
        for (int i = 0; i < gos.Length; i++)
        {
            Constants.SceneObject so;
            so.name = gos[i].name;
            so.position = new float[] { gos[i].transform.position.x,
                gos[i].transform.position.y, gos[i].transform.position.z };
            so.tag = gos[i].tag;
            sos[i] = so;
        }
        
        // return the json to publish
        return RosbridgeUtilities.GetROSJsonPublishSceneMsg(Constants.SCENE_ROSTOPIC,
            (backg.name == null ? "" : backg.name), sos);
    }
    
    /// <summary>
    /// Handles log message events
    /// </summary>
    /// <param name="sender">sender</param>
    /// <param name="logme">event to log</param>
    void HandleLogEvent (object sender, LogEvent logme)
    {
        switch(logme.type)
        {
        case LogEvent.EventType.Action:
            // note that for some gestures, the 2d Point returned by the gesture
            // library does not include z position and sets z to 0 by default, so
            // the z position may not be accurate (but it also doesn't really matter)
            this.clientSocket.SendMessage(RosbridgeUtilities.GetROSJsonPublishActionMsg(
                Constants.ACTION_ROSTOPIC, logme.name, logme.action, 
                (logme.position.HasValue ? new float[] 
                {logme.position.Value.x, logme.position.Value.y,
                logme.position.Value.z} : null), System.DateTime.Now.ToUniversalTime().Subtract(
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds));
            break;
            
        case LogEvent.EventType.Scene:
            Debug.LogWarning("Log scene event not implemented yet!"); //TODO send keyframes
            break;
            
        case LogEvent.EventType.Message:
            this.clientSocket.SendMessage(RosbridgeUtilities.GetROSJsonPublishStringMsg(
            Constants.LOG_ROSTOPIC, logme.state));
            break;
        
        }
        
    }
    

}
