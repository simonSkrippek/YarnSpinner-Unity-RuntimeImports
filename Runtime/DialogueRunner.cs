﻿/*

The MIT License (MIT)

Copyright (c) 2015-2017 Secret Lab Pty. Ltd. and Yarn Spinner contributors.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using System.Threading.Tasks;

namespace Yarn.Unity
{

    /// <summary>
    /// The [DialogueRunner]({{|ref
    /// "/docs/unity/components/dialogue-runner.md"|}}) component acts as
    /// the interface between your game and Yarn Spinner.
    /// </summary>
    [AddComponentMenu("Scripts/Yarn Spinner/Dialogue Runner")]
    public class DialogueRunner : MonoBehaviour
    {
        /// <summary>
        /// The <see cref="YarnProgram"/> assets that should be loaded on
        /// scene start.
        /// </summary>
        public YarnProgram[] yarnScripts;

        /// <summary>
        /// The variable storage object.
        /// </summary>
        public VariableStorageBehaviour variableStorage;

        /// <summary>
        /// The View classes that will present the dialogue to the user.
        /// </summary>
        public DialogueViewBase[] dialogueViews;

        /// <summary>The name of the node to start from.</summary>
        /// <remarks>
        /// This value is used to select a node to start from when
        /// <see cref="StartDialogue"/> is called.
        /// </remarks>
        public string startNode = Yarn.Dialogue.DEFAULT_START;

        /// <summary>
        /// Whether the DialogueRunner should automatically start running
        /// dialogue after the scene loads.
        /// </summary>
        /// <remarks>
        /// The node specified by <see cref="startNode"/> will be used.
        /// </remarks>
        public bool startAutomatically = true;

        /// <summary>
        /// Whether the DialogueRunner should automatically proceed to the
        /// next line once a line has been finished.
        /// </summary>
        public bool continueNextLineOnLineFinished;

        /// <summary>
        /// If true, when an option is selected, it's as though it were a line.
        /// </summary>
        public bool runSelectedOptionAsLine;

        public LineProviderBehaviour lineProvider;

        /// <summary>
        /// Gets a value that indicates if the dialogue is actively running.
        /// </summary>
        public bool IsDialogueRunning { get; set; }

        /// <summary>
        /// A type of <see cref="UnityEvent"/> that takes a single string
        /// parameter. 
        /// </summary>
        /// <remarks>
        /// A concrete subclass of <see cref="UnityEvent"/> is needed in
        /// order for Unity to serialise the type correctly.
        /// </remarks>
        [Serializable]
        public class StringUnityEvent : UnityEvent<string> { }

        /// <summary>
        /// A Unity event that is called when a node starts running.
        /// </summary>
        /// <remarks>
        /// This event receives as a parameter the name of the node that is
        /// about to start running.
        /// </remarks>
        /// <seealso cref="Dialogue.NodeStartHandler"/>
        public StringUnityEvent onNodeStart;
        
        /// <summary>
        /// A Unity event that is called when a node is complete.
        /// </summary>
        /// <remarks>
        /// This event receives as a parameter the name of the node that
        /// just finished running.
        /// </remarks>
        /// <seealso cref="Dialogue.NodeCompleteHandler"/>
        public StringUnityEvent onNodeComplete;

        /// <summary>
        /// A Unity event that is called once the dialogue has completed.
        /// </summary>
        /// <seealso cref="Dialogue.DialogueCompleteHandler"/>
        public UnityEvent onDialogueComplete;

        /// <summary>
        /// Gets the name of the current node that is being run.
        /// </summary>
        /// <seealso cref="Dialogue.currentNode"/>
        public string CurrentNodeName => Dialogue.currentNode;

        /// <summary>
        /// Gets the underlying <see cref="Dialogue"/> object that runs the
        /// Yarn code.
        /// </summary>
        public Dialogue Dialogue => dialogue ?? (dialogue = CreateDialogueInstance());

        /// <summary>
        /// A <see cref="StringUnityEvent"/> that is called when a <see
        /// cref="Command"/> 
        /// is received.
        /// </summary>
        /// <remarks>
        /// Use this method to dispatch a command to other parts of your
        /// game. This method is only called if the <see cref="Command"/>
        /// has not been handled by a command handler that has been added
        /// to the
        /// <see cref="DialogueRunner"/>, or by a method on a <see
        /// cref="MonoBehaviour"/> in the scene with the attribute <see
        /// cref="YarnCommandAttribute"/>. {{|note|}} When a command is
        /// delivered in this way, the <see cref="DialogueRunner"/> 
        /// will not pause execution. If you want a command to make the
        /// DialogueRunner pause execution, see <see
        /// cref="AddCommandHandler(string, BlockingCommandHandler)"/>.
        /// {{|/note|}}
        /// 
        /// This method receives the full text of the command, as it
        /// appears between the `<![CDATA[<<]]>` and `<![CDATA[>>]]>`
        /// markers.
        /// </remarks>
        /// <seealso cref="AddCommandHandler(string, CommandHandler)"/>
        /// <seealso cref="AddCommandHandler(string,
        /// BlockingCommandHandler)"/>
        /// <seealso cref="YarnCommandAttribute"/>
        public StringUnityEvent onCommand;

        /// <summary>
        /// The collection of registered YarnCommand-tagged methods.
        /// Populated in the <see cref="InitializeClass"/> method.
        /// </summary>
        private static Dictionary<string, MethodInfo> _yarnCommands = new Dictionary<string, MethodInfo>();
        
        /// <summary>
        /// Finds all MonoBehaviour types in the loaded assemblies, and
        /// looks for all methods that are tagged with YarnCommand.
        /// </summary>
        [RuntimeInitializeOnLoadMethod]
        static void InitializeClass() {

            // Find all assemblies
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            // In each assembly, find all types that descend from MonoBehaviour
            foreach (var assembly in allAssemblies) {
                foreach (var type in assembly.GetTypes()) {

                    // We only care about MonoBehaviours
                    if (typeof(MonoBehaviour).IsAssignableFrom(type) == false) {
                        continue;
                    }

                    // Find all methods on each type that have the YarnCommand attribute
                    foreach (var method in type.GetMethods()) {
                        var attributes = new List<YarnCommandAttribute>(method.GetCustomAttributes<YarnCommandAttribute>());
                        
                        if (attributes.Count > 0) {
                            // This method has the YarnCommand attribute!
                            // The compiler enforces a single attribute of
                            // this type on each members, so if we have n >
                            // 0, n == 1.
                            var att = attributes[0];

                            var name = att.CommandString;

                            Debug.Log($"Registered command {name} to {method.DeclaringType.FullName}.{method.Name}");

                            try
                            {
                                // Cache the methodinfo
                                _yarnCommands.Add(name, method);
                            }
                            catch (ArgumentException)
                            {
                                MethodInfo existingDefinition = _yarnCommands[name];
                                Debug.LogError($"Can't add {method.DeclaringType.FullName}.{method.Name} for command {name} because it's already defined on {existingDefinition.DeclaringType.FullName}.{existingDefinition.Name}");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds a program, and parses and adds the contents of the
        /// program's string table to the DialogueRunner's combined string
        /// table.
        /// </summary>
        /// <remarks>This method calls <see
        /// cref="AddDialogueLines(YarnProgram)"/> to load the string table
        /// for the current localisation. It selects the appropriate string
        /// table based on the value of set in the Preferences dialogue.
        public void Add(YarnProgram scriptToLoad)
        {
            Dialogue.AddProgram(scriptToLoad.GetProgram());         

            if (lineProviderIsTemporary) {
                var stringTableAsset = scriptToLoad.baseLocalisationStringTable;
                if (stringTableAsset == null) {
                    Debug.LogWarning($"Yarn script {scriptToLoad.name} doesn't have a base localization string table. This dialogue runner should be set up to use the {nameof(LocalizationDatabase)} that the Yarn scripts are associated with instead.");
                    return;
                }
                var stringTableEntries = StringTableEntry.ParseFromCSV(scriptToLoad.baseLocalisationStringTable.text);
                lineProvider.localizationDatabase.GetLocalization(scriptToLoad.baseLocalizationId).AddLocalizedStrings(stringTableEntries);
            }   
        }

        /// <summary>
        /// Starts running dialogue. The node specified by <see
        /// cref="startNode"/> will start running.
        /// </summary>
        public void StartDialogue() => StartDialogue(startNode);

        /// <summary>
        /// Start the dialogue from a specific node.
        /// </summary>
        /// <param name="startNode">The name of the node to start running
        /// from.</param>
        public void StartDialogue(string startNode)
        {
            // Stop any processes that might be running already
            foreach (var dialogueView in dialogueViews) {
                if (dialogueView == null) continue;

                dialogueView.StopAllCoroutines();
            }

            // Get it going
            RunDialogue();
            void RunDialogue()
            {
                // Mark that we're in conversation.
                IsDialogueRunning = true;

                // Signal that we're starting up.
                foreach (var dialogueView in dialogueViews) {
                    if (dialogueView == null) continue;

                    dialogueView.DialogueStarted();
                }

                // Request that the dialogue select the current node. This
                // will prepare the dialogue for running; as a side effect,
                // our prepareForLines delegate may be called.
                Dialogue.SetNode(startNode);
                
                if (lineProvider.LinesAvailable == false) {
                    // The line provider isn't ready to give us our lines
                    // yet. We need to start a coroutine that waits for
                    // them to finish loading, and then runs the dialogue.
                    StartCoroutine(ContinueDialogueWhenLinesAvailable());
                } else {
                    ContinueDialogue();
                }
            }
        }

        private IEnumerator ContinueDialogueWhenLinesAvailable() {
            // Wait until lineProvider.LinesAvailable becomes true
            while (lineProvider.LinesAvailable == false) {
                yield return null;
            }

            // And then run our dialogue.
            ContinueDialogue();
        }

        /// <summary>
        /// Resets the <see cref="variableStorage"/>, and starts running the dialogue again from the node named <see cref="startNode"/>.
        /// </summary>        
        public void ResetDialogue()
        {
            variableStorage.ResetToDefaults();
            StartDialogue();
        }

        /// <summary>
        /// Unloads all nodes from the <see cref="dialogue"/>.
        /// </summary>
        public void Clear()
        {
            Assert.IsFalse(IsDialogueRunning, "You cannot clear the dialogue system while a dialogue is running.");
            Dialogue.UnloadAll();
        }

        /// <summary>
        /// Stops the <see cref="dialogue"/>.
        /// </summary>
        public void Stop()
        {
            IsDialogueRunning = false;
            Dialogue.Stop();
        }

        /// <summary>
        /// Returns `true` when a node named `nodeName` has been loaded.
        /// </summary>
        /// <param name="nodeName">The name of the node.</param>
        /// <returns>`true` if the node is loaded, `false`
        /// otherwise/</returns>
        public bool NodeExists(string nodeName) => Dialogue.NodeExists(nodeName);

        /// <summary>
        /// Returns the collection of tags that the node associated with
        /// the node named `nodeName`.
        /// </summary>
        /// <param name="nodeName">The name of the node.</param>
        /// <returns>The collection of tags associated with the node, or
        /// `null` if no node with that name exists.</returns>
        public IEnumerable<string> GetTagsForNode(String nodeName) => Dialogue.GetTagsForNode(nodeName);

        /// <summary>
        /// Adds a command handler. Dialogue will continue running after
        /// the command is called.
        /// </summary>
        /// <remarks>
        /// When this command handler has been added, it can be called from
        /// your Yarn scripts like so:
        ///
        /// <![CDATA[```yarn
        /// <<commandName param1 param2>>
        /// ```]]>
        ///
        /// When this command handler is called, the DialogueRunner will
        /// not stop executing code.
        /// </remarks>
        /// <param name="commandName">The name of the command.</param>
        /// <param name="handler">The <see cref="CommandHandler"/> that
        /// will be invoked when the command is called.</param>
        public void AddCommandHandler(string commandName, CommandHandler handler)
        {
            if (commandHandlers.ContainsKey(commandName) || blockingCommandHandlers.ContainsKey(commandName)) {
                Debug.LogError($"Cannot add a command handler for {commandName}: one already exists");
                return;
            }
            commandHandlers.Add(commandName, handler);
        }

        /// <summary>
        /// Adds a command handler. Dialogue will pause execution after the
        /// command is called.
        /// </summary>
        /// <remarks>
        /// When this command handler has been added, it can be called from
        /// your Yarn scripts like so:
        ///
        /// <![CDATA[
        /// ```yarn
        /// <<commandName param1 param2>>
        /// ```
        /// ]]>
        ///
        /// When this command handler is called, the DialogueRunner will
        /// stop executing code. The <see cref="BlockingCommandHandler"/>
        /// will receive an <see cref="Action"/> to call when it is ready
        /// for the Dialogue Runner to resume executing code.
        /// </remarks>
        /// <param name="commandName">The name of the command.</param>
        /// <param name="handler">The <see cref="CommandHandler"/> that
        /// will be invoked when the command is called.</param>
        public void AddCommandHandler(string commandName, BlockingCommandHandler handler)
        {
            if (commandHandlers.ContainsKey(commandName) || blockingCommandHandlers.ContainsKey(commandName)) {
                Debug.LogError($"Cannot add a command handler for {commandName}: one already exists");
                return;
            }
            blockingCommandHandlers.Add(commandName, handler);
        }

        /// <summary>
        /// Removes a command handler.
        /// </summary>
        /// <param name="commandName">The name of the command to remove.</param>
        public void RemoveCommandHandler(string commandName)
        {
            commandHandlers.Remove(commandName);
            blockingCommandHandlers.Remove(commandName);
        }

        /// <summary>
        /// Add a new function that returns a value, so that it can be
        /// called from Yarn scripts.
        /// </summary>        
        /// <remarks>
        /// When this function has been registered, it can be called from
        /// your Yarn scripts like so:
        /// 
        /// <![CDATA[
        /// ```yarn
        /// <<if myFunction(1, 2) == true>>
        ///     myFunction returned true!
        /// <<endif>>
        /// ```
        /// ]]>
        /// 
        /// The `call` command can also be used to invoke the function:
        /// 
        /// <![CDATA[
        /// ```yarn
        /// <<call myFunction(1, 2)>>
        /// ```
        /// ]]>    
        /// </remarks>
        /// <param name="implementation">The <see cref="Delegate"/>
        /// that should be invoked when this function is called.</param>
        /// <seealso cref="Library"/> 
        public void AddFunction(string name, Delegate implementation)
        {
            if (Dialogue.library.FunctionExists(name)) {
                Debug.LogError($"Cannot add function {name}: one already exists");
                return;
            }

            Dialogue.library.RegisterFunction(name, implementation);
        }

        public void AddFunction<TResult>(string name, System.Func<TResult> implementation) {
            AddFunction(name, (Delegate) implementation);
        }

        public void AddFunction<TResult, T1>(string name, System.Func<TResult, T1> implementation) {
            AddFunction(name, (Delegate) implementation);
        }

        public void AddFunction<TResult, T1, T2>(string name, System.Func<TResult, T1, T2> implementation) {
            AddFunction(name, (Delegate) implementation);
        }

        public void AddFunction<TResult, T1, T2, T3>(string name, System.Func<TResult, T1, T2, T3> implementation) {
            AddFunction(name, (Delegate) implementation);
        }

        public void AddFunction<TResult, T1, T2, T3, T4>(string name, System.Func<TResult, T1, T2, T3, T4> implementation) {
            AddFunction(name, (Delegate) implementation);
        }

        public void AddFunction<TResult, T1, T2, T3, T4, T5>(string name, System.Func<TResult, T1, T2, T3, T4, T5> implementation) {
            AddFunction(name, (Delegate) implementation);
        }

        public void AddFunction<TResult, T1, T2, T3, T4, T5, T6>(string name, System.Func<TResult, T1, T2, T3, T4, T5, T6> implementation) {
            AddFunction(name, (Delegate) implementation);
        }

        /// <summary>
        /// Remove a registered function.
        /// </summary>
        /// <remarks>
        /// After a function has been removed, it cannot be called from
        /// Yarn scripts.
        /// </remarks>
        /// <param name="name">The name of the function to remove.</param>
        /// <seealso cref="AddFunction(string, int, Function)"/>
        /// <seealso cref="AddFunction(string, int, ReturningFunction)"/>
        public void RemoveFunction(string name) => Dialogue.library.DeregisterFunction(name);

        #region Private Properties/Variables/Procedures

        /// <summary>
        /// The <see cref="LocalizedLine"/> currently being displayed on
        /// the dialogue views.
        /// </summary>
        internal LocalizedLine CurrentLine {get;private set;}

        /// <summary>
        ///  The collection of dialogue views that are currently either
        ///  delivering a line, or dismissing a line from being on screen.
        /// </summary>
        private readonly HashSet<DialogueViewBase> ActiveDialogueViews = new HashSet<DialogueViewBase>();

        Action<int> selectAction;

        /// <summary>
        /// Represents a method that can be called when the DialogueRunner
        /// encounters a command. 
        /// </summary>
        /// <remarks>
        /// After this method returns, the DialogueRunner will continue
        /// executing code.
        /// </remarks>
        /// <param name="parameters">The list of parameters that this
        /// command was invoked with.</param>
        /// <seealso cref="AddCommandHandler(string, CommandHandler)"/>
        /// <seealso cref="AddCommandHandler(string,
        /// BlockingCommandHandler)"/>
        public delegate void CommandHandler(string[] parameters);

        /// <summary>
        /// Represents a method that can be called when the DialogueRunner
        /// encounters a command. 
        /// </summary>
        /// <remarks>
        /// After this method returns, the DialogueRunner will pause
        /// executing code. The `onComplete` delegate will cause the
        /// DialogueRunner to resume executing code.
        /// </remarks>
        /// <param name="parameters">The list of parameters that this
        /// command was invoked with.</param>
        /// <param name="onComplete">The method to call when the
        /// DialogueRunner should continue executing code.</param>
        /// <seealso cref="AddCommandHandler(string, CommandHandler)"/>
        /// <seealso cref="AddCommandHandler(string,
        /// BlockingCommandHandler)"/>
        public delegate void BlockingCommandHandler(string[] parameters, Action onComplete);

        /// Maps the names of commands to action delegates.
        Dictionary<string, CommandHandler> commandHandlers = new Dictionary<string, CommandHandler>();
        Dictionary<string, BlockingCommandHandler> blockingCommandHandlers = new Dictionary<string, BlockingCommandHandler>();

        // A flag used to note when we call into a blocking command
        // handler, but it calls its complete handler immediately -
        // _before_ the Dialogue is told to pause. This out-of-order
        // problem can lead to the Dialogue being stuck in a paused state.
        // To solve this, this variable is set to false before any blocking
        // command handler is called, and set to true when ContinueDialogue
        // is called. If it's true after calling a blocking command
        // handler, then the Dialogue is not told to pause.
        bool wasCompleteCalled = false;

        /// Our conversation engine
        /** Automatically created on first access
         */
        Dialogue dialogue;

        // If true, lineProvider was created at runtime, and will be empty.
        // Calls to Add() should insert line content into it.
        private bool lineProviderIsTemporary = false;

        // The current set of options that we're presenting. Null if we're
        // not currently presenting options.
        private OptionSet currentOptions;

        void Awake() {
            if (lineProvider == null) {
                // If we don't have a line provider, we don't have a way to
                // access a LocalizationDatabase and fetch information for
                // other localizations, or to fetch localized assets like
                // audio clips for voiceover. In this situation, we'll fall
                // back to a really simple setup: we'll create a temporary
                // TextLineProvider, create a temporary
                // LocalizationDatabase, and set it up with the
                // YarnPrograms we know about.

                // TODO: decide what to do about determining the
                // localization of runtime-provided YarnPrograms. Make it a
                // parameter on an AddCompiledProgram method?

                // Create the temporary line provider and the localization database
                lineProvider = gameObject.AddComponent<TextLineProvider>();
                lineProvider.localizationDatabase = ScriptableObject.CreateInstance<LocalizationDatabase>();
                var runtimeLocalization = lineProvider.localizationDatabase.CreateLocalization(Preferences.TextLanguage);

                lineProviderIsTemporary = true;

                // Let the user know what we're doing.
                Debug.Log("Dialogue Runner has no LineProvider; setting a temporary one up with the base text found inside the scripts.");

                // Fill the localization database with the lines found in the scripts
                foreach (var program in yarnScripts) {
                    
                    // In order to get the text of the base localization
                    // for this script, we need to parse the base
                    // localization CSV text asset associated with this
                    // script. (The text asset will only be included in the
                    // build if the YarnImporter determines that the
                    // YarnProgram has no LocalizationDatabase assigned.)

                    if (program.baseLocalisationStringTable == null) {
                        Debug.LogWarning($"No base localization string table was included for the Yarn script {program.name}. It may be connected to a {nameof(LocalizationDatabase)}. You should set this {nameof(DialogueRunner)} up with a Line Provider, and connect the Line Provider to the LocalizationDatabase.");
                        continue;
                    }

                    // Extract the text for the base localization.
                    var text = program.baseLocalisationStringTable.text;

                    // Parse it into string table entries.
                    var parsedStringTableEntries = StringTableEntry.ParseFromCSV(text);

                    // Add it to the runtime localization.
                    runtimeLocalization.AddLocalizedStrings(parsedStringTableEntries);
                }
            }
        }

        /// Start the dialogue
        void Start()
        {
            Assert.IsNotNull(dialogueViews, "No View class (like DialogueUI) was given! Can't run the dialogue without a View class!");
            Assert.IsNotNull(variableStorage, "Variable storage was not set! Can't run the dialogue!");

            // Give each dialogue view the continuation action, which
            // they'll call to pass on the user intent to move on to the
            // next line (or interrupt the current one).
            System.Action continueAction = OnViewUserIntentNextLine;
            foreach (var dialogueView in dialogueViews) {
                if (dialogueView == null)
                {
                    Debug.LogWarning("The 'Dialogue Views' field contains a NULL element.", gameObject);
                    continue;
                }

                dialogueView.onUserWantsLineContinuation = continueAction;
            }

            // Ensure that the variable storage has the right stuff in it
            variableStorage.ResetToDefaults();

            // Combine all scripts together and load them
            if (yarnScripts != null && yarnScripts.Length > 0) {

                var compiledPrograms = new List<Program>();

                foreach (var program in yarnScripts) {
                    compiledPrograms.Add(program.GetProgram());
                }

                var combinedProgram = Program.Combine(compiledPrograms.ToArray());

                Dialogue.SetProgram(combinedProgram);
            }

            if (startAutomatically) {
                StartDialogue();
            }
        }

        Dialogue CreateDialogueInstance()
        {
            // Create the main Dialogue runner, and pass our
            // variableStorage to it
            var dialogue = new Yarn.Dialogue(variableStorage) {

                // Set up the logging system.
                LogDebugMessage = delegate (string message) {
                    Debug.Log(message);
                },
                LogErrorMessage = delegate (string message) {
                    Debug.LogError(message);
                },

                lineHandler = HandleLine,
                commandHandler = HandleCommand,
                optionsHandler = HandleOptions,
                nodeStartHandler = (node) => {
                    onNodeStart?.Invoke(node);
                    return Dialogue.HandlerExecutionType.ContinueExecution;
                },
                nodeCompleteHandler = (node) => {
                    onNodeComplete?.Invoke(node);
                    return Dialogue.HandlerExecutionType.ContinueExecution;
                },
                dialogueCompleteHandler = HandleDialogueComplete,
                prepareForLinesHandler = PrepareForLines,
            };

            // Yarn Spinner defines two built-in commands: "wait",
            // and "stop". Stop is defined inside the Virtual
            // Machine (the compiler traps it and makes it a
            // special case.) Wait is defined here in Unity.
            AddCommandHandler("wait", HandleWaitCommand);

            selectAction = SelectedOption;

            return dialogue;

            void HandleWaitCommand(string[] parameters, Action onComplete)
            {
                if (parameters?.Length != 1) {
                    Debug.LogErrorFormat("<<wait>> command expects one parameter.");
                    onComplete();
                    return;
                }

                string durationString = parameters[0];

                if (float.TryParse(durationString,
                                   System.Globalization.NumberStyles.AllowDecimalPoint,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out var duration) == false) {

                    Debug.LogErrorFormat($"<<wait>> failed to parse duration {durationString}");
                    onComplete();
                }

                StartCoroutine(DoHandleWait());
                IEnumerator DoHandleWait()
                {
                    yield return new WaitForSeconds(duration);
                    onComplete();
                }
            }

            void HandleOptions(OptionSet options) {
                currentOptions = options;

                DialogueOption[] optionSet = new DialogueOption[options.Options.Length];
                for (int i = 0; i < options.Options.Length; i++) {

                    // Localize the line associated with the option
                    var localisedLine = lineProvider.GetLocalizedLine(options.Options[i].Line);
                    localisedLine.Text = Dialogue.ParseMarkup(localisedLine.RawText);

                    optionSet[i] = new DialogueOption {
                        TextID = options.Options[i].Line.ID,
                        DialogueOptionID = options.Options[i].ID,
                        Line = localisedLine,
                    };
                }
                foreach (var dialogueView in dialogueViews) {
                    if (dialogueView == null) continue;

                    dialogueView.RunOptions(optionSet, selectAction);
                }
            }

            void HandleDialogueComplete()
            {
                IsDialogueRunning = false;
                foreach (var dialogueView in dialogueViews) {
                    if (dialogueView == null) continue;

                    dialogueView.DialogueComplete();
                }
                onDialogueComplete.Invoke();
            }

            Dialogue.HandlerExecutionType HandleCommand(Command command)
            {
                bool wasValidCommand;
                Dialogue.HandlerExecutionType executionType;

                // Try looking in the command handlers first, which is a
                // lot cheaper than crawling the game object hierarchy.

                // Set a flag that we can use to tell if the dispatched
                // command immediately called _continue
                wasCompleteCalled = false;

                (wasValidCommand, executionType) = DispatchCommandToRegisteredHandlers(command, () => ContinueDialogue());

                if (wasValidCommand) {

                    // This was a valid command. It returned either
                    // continue, or pause; if it returned pause, there's a
                    // chance that the command handler immediately called
                    // _continue, in which case we should not pause.
                    if (wasCompleteCalled) {
                        return Dialogue.HandlerExecutionType.ContinueExecution;
                    }
                    else {
                        // Either continue execution, or pause (in which case
                        // _continue will be called)
                        return executionType;
                    }
                }

                // We didn't find it in the comand handlers. Try looking in
                // the game objects.
                (wasValidCommand, executionType) = DispatchCommandToGameObject(command);

                if (wasValidCommand) {
                    // We found an object and method to invoke as a Yarn
                    // command. It may or may not have been a coroutine; if
                    // it was a coroutine, executionType will be
                    // HandlerExecutionType.Pause, and we'll wait for it to
                    // complete before resuming execution.
                    return executionType;
                }

                // We didn't find a method in our C# code to invoke. Try
                // invoking on the publicly exposed UnityEvent.
                onCommand?.Invoke(command.Text);
                return Dialogue.HandlerExecutionType.ContinueExecution;
            }

            /// Forward the line to the dialogue UI.
            Dialogue.HandlerExecutionType HandleLine(Line line)
            {
                // Get the localized line from our line provider
                CurrentLine = lineProvider.GetLocalizedLine(line);

                // Render the markup
                CurrentLine.Text = Dialogue.ParseMarkup(CurrentLine.RawText);

                CurrentLine.Status = LineStatus.Running;

                // Clear the set of active dialogue views, just in case
                ActiveDialogueViews.Clear();

                // Send line to available dialogue views
                foreach (var dialogueView in dialogueViews) {
                    if (dialogueView == null) continue;

                    // Mark this dialogue view as active                
                    ActiveDialogueViews.Add(dialogueView);
                    dialogueView.RunLine(CurrentLine, 
                        () => DialogueViewCompletedDelivery(dialogueView));
                }
                return Dialogue.HandlerExecutionType.PauseExecution;
            }

            /// Indicates to the DialogueRunner that the user has selected
            /// an option
            void SelectedOption(int obj)
            {
                // Mark that this is the currently selected option in the
                // Dialogue
                Dialogue.SetSelectedOption(obj);

                if (runSelectedOptionAsLine) {
                    foreach (var option in currentOptions.Options) {
                        if (option.ID == obj) {
                            HandleLine(option.Line);
                            return;
                        }
                    }

                    Debug.LogError($"Can't run selected option ({obj}) as a line: couldn't find the option's associated {nameof(Line)} object");
                    ContinueDialogue();
                } else {
                    ContinueDialogue();
                }
                
            }

            (bool commandWasFound, Dialogue.HandlerExecutionType executionType) DispatchCommandToRegisteredHandlers(Command command, Action onComplete)
            {
                var commandTokens = command.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                //Debug.Log($"Command: <<{command.Text}>>");

                if (commandTokens.Length == 0) {
                    // Nothing to do
                    return (false, Dialogue.HandlerExecutionType.ContinueExecution);
                }

                var firstWord = commandTokens[0];

                if (commandHandlers.ContainsKey(firstWord) == false &&
                    blockingCommandHandlers.ContainsKey(firstWord) == false) {

                    // We don't have a registered handler for this command,
                    // but some other part of the game might.
                    return (false, Dialogue.HandlerExecutionType.ContinueExecution);
                }

                // Single-word command, eg <<jump>>
                if (commandTokens.Length == 1) {
                    if (commandHandlers.ContainsKey(firstWord)) {
                        commandHandlers[firstWord](null);
                        return (true, Dialogue.HandlerExecutionType.ContinueExecution);
                    }
                    else {
                        blockingCommandHandlers[firstWord](new string[] { }, onComplete);
                        return (true, Dialogue.HandlerExecutionType.PauseExecution);
                    }
                }

                // Multi-word command, eg <<walk Mae left>>
                var remainingWords = new string[commandTokens.Length - 1];

                // Copy everything except the first word from the array
                System.Array.Copy(commandTokens, 1, remainingWords, 0, remainingWords.Length);

                if (commandHandlers.ContainsKey(firstWord)) {
                    commandHandlers[firstWord](remainingWords);
                    return (true, Dialogue.HandlerExecutionType.ContinueExecution);
                }
                else {
                    blockingCommandHandlers[firstWord](remainingWords, onComplete);
                    return (true, Dialogue.HandlerExecutionType.PauseExecution);
                }
            }
        }

        /// <summary>
        /// Parses the command string inside <paramref
        /// name="command"/>, attempts to locate a suitable method on a
        /// suitable game object, and the invokes the method.
        /// </summary>
        /// <param name="command">The <see cref="Command"/> to
        /// run.</param>
        /// <returns>A 2-tuple: the first component is true if a method
        /// was invoked, and the second component indicates whether the
        /// Dialogue should suspend execution or continue
        /// executing.</returns>
        internal (bool methodFound, Dialogue.HandlerExecutionType executionType) DispatchCommandToGameObject(Command command)
        {
            // Call out to the string version of this method, because
            // Yarn.Command's constructor is only accessible from inside
            // Yarn Spinner, but we want to be able to unit test. So, we
            // extract it, and call the underlying implementation, which is
            // testable.
            return DispatchCommandToGameObject(command.Text);
        }

        /// <inheritdoc cref="DispatchCommandToGameObject(Command)"/>
        /// <param name="command">The text of the command to dispatch.</param>
        internal (bool methodFound, Dialogue.HandlerExecutionType executionType) DispatchCommandToGameObject(string command)
        {
        
            // Start by splitting our command string by spaces.
            var words = command.Split(' ');

            // We need 2 parameters in order to have both a command
            // name, and the name of an object to find.
            if (words.Length < 2)
            {
                // Don't log an error, because the dialogue views might
                // handle this command.
                return (false, Dialogue.HandlerExecutionType.ContinueExecution);
            }

            // Get our command name and object name.
            var commandName = words[0];
            var objectName = words[1];

            if (_yarnCommands.ContainsKey(commandName) == false)
            {
                // We didn't find a MethodInfo to invoke for this
                // command, so we can't dispatch it. Don't log an error
                // for it, because this command may be handled by our
                // DialogueViews.
                return (false, Dialogue.HandlerExecutionType.ContinueExecution);
            }

            // Attempt to find the object with this name.
            var sceneObject = GameObject.Find(objectName);

            if (sceneObject == null)
            {
                // If we can't find an object, we can't dispatch a
                // command. Log an error here, because this command has
                // been registered with the YarnCommand system, but the
                // object the script calls for doesn't exist.
                Debug.LogError($"Can't run command {commandName} on {objectName}: an object with that name doesn't exist in the scene.");

                return (false, Dialogue.HandlerExecutionType.ContinueExecution);
            }

            var methodInfo = _yarnCommands[commandName];

            // If sceneObject has a component whose type matches the
            // methodInfo, we can invoke that method on it.
            var target = sceneObject.GetComponent(methodInfo.DeclaringType) as MonoBehaviour;

            if (target == null)
            {
                Debug.LogError($"Can't run command {commandName} on {objectName}: the command is only defined on {methodInfo.DeclaringType.FullName} components, but {objectName} doesn't have one.");
                return (false, Dialogue.HandlerExecutionType.ContinueExecution);
            }

            // Next test: was the right number of parameters provided?
            ParameterInfo[] methodParameters = methodInfo.GetParameters();
            List<string> parameters;

            if (words.Length > 2)
            {
                parameters = new List<string>(words);
                // remove the first two - they're the command name and
                // the object name
                parameters.RemoveRange(0, 2);
            }
            else
            {
                // No words in the command beyond the first two, which
                // aren't counted as parameters, so just use an empty
                // list
                parameters = new List<string>();
            }

            var requiredParameters = 0;
            var optionalParameters = 0;

            // How many optional and non-optional parameters does the
            // method have?
            foreach (var parameter in methodParameters)
            {
                if (parameter.IsOptional)
                {
                    optionalParameters += 1;
                }
                else
                {
                    requiredParameters += 1;
                }
            }

            bool anyOptional = optionalParameters > 0;

            // We can't run the command if we didn't supply the right
            // number of parameters.
            if (anyOptional)
            {
                if (parameters.Count < requiredParameters || parameters.Count > (requiredParameters + optionalParameters))
                {
                    Debug.LogError($"Can't run command {commandName}: {methodInfo.Name} requires between {requiredParameters} and {requiredParameters + optionalParameters} parameters, but {parameters.Count} were provided.");
                    return (false, Dialogue.HandlerExecutionType.ContinueExecution);
                }
            }
            else
            {
                if (parameters.Count != requiredParameters)
                {
                    Debug.LogError($"Can't run command {commandName}: {methodInfo.Name} requires {requiredParameters} parameters, but {parameters.Count} were provided.");
                    return (false, Dialogue.HandlerExecutionType.ContinueExecution);
                }
            }

            // Make a list of objects that we'll supply as parameters
            // to the method when we invoke it.
            var finalParameters = new object[requiredParameters + optionalParameters];

            // Final check: convert each supplied parameter from a
            // string to the expected type.
            for (int i = 0; i < finalParameters.Length; i++)
            {

                if (i >= parameters.Count) {
                    // We didn't supply a parameter here, so supply
                    // Type.Missing to make it use the default value
                    // instead.
                    finalParameters[i] = System.Type.Missing;
                    continue;
                }

                var expectedType = methodParameters[i].ParameterType;

                // Two special case: if the method expects a GameObject
                // or a Component (or Component-derived type), locate
                // that object and supply it. The object, or the object
                // the desired component is on, must be active. If this
                // fails, supply null.
                if (typeof(GameObject).IsAssignableFrom(expectedType))
                {
                    finalParameters[i] = GameObject.Find(parameters[i]);
                }
                else if (typeof(Component).IsAssignableFrom(expectedType))
                {
                    // Find the game object with the component we're
                    // looking for
                    var go = GameObject.Find(parameters[i]);
                    if (go != null)
                    {
                        // Find the component on this game object (or
                        // null)
                        var c = go.GetComponentInChildren(expectedType);
                        finalParameters[i] = c;
                    }
                }
                else
                {
                    // Attempt to perform a straight conversion, using
                    // the invariant culture. The parameter type must
                    // implement IConvertible.
                    try {
                        finalParameters[i] = Convert.ChangeType(parameters[i], expectedType, System.Globalization.CultureInfo.InvariantCulture);
                    } catch (Exception e) {
                        Debug.LogError($"Can't run command \"{command}\": can't convert parameter {i+1} (\"{parameters[i]}\") to {expectedType}: {e}");
                        return (false, Dialogue.HandlerExecutionType.ContinueExecution);
                    }
                    
                }
            }

            // We're finally ready to invoke the method on the object!

            // Before we invoke it, we need to know if this is a
            // coroutine. It's a coroutine if the method returns an
            // IEnumerator.

            var isCoroutine = methodInfo.ReturnType == typeof(IEnumerator);

            if (isCoroutine)
            {
                // Start the coroutine.
                StartCoroutine(DoYarnCommand(target, methodInfo, finalParameters));

                // Indicate that we ran a command, and should pause
                // execution. DoYarnCommand will call Continue() later,
                // when the coroutine completes.
                return (true, Dialogue.HandlerExecutionType.PauseExecution);
            }
            else
            {
                // Invoke it directly.
                methodInfo.Invoke(target, finalParameters);

                // Indicate that we ran the command, and should
                // continue execution.
                return (true, Dialogue.HandlerExecutionType.ContinueExecution);
            }

            IEnumerator DoYarnCommand(MonoBehaviour component,
                                            MethodInfo method,
                                            object[] localParameters)
            {
                // Wait for this command coroutine to complete
                yield return StartCoroutine((IEnumerator)method.Invoke(component, localParameters));

                // And then continue running dialogue
                ContinueDialogue();
            }
        }

        private void PrepareForLines(IEnumerable<string> lineIDs)
        {
            lineProvider.PrepareForLines(lineIDs);
        }

        /// <summary>
        /// Called when a <see cref="DialogueViewBase"/> has finished
        /// delivering its line. When all views in <see
        /// cref="ActiveDialogueViews"/> have called this method, the
        /// line's status will change to <see
        /// cref="LineStatus.Delivered"/>.
        /// </summary>
        /// <param name="dialogueView">The view that finished delivering
        /// the line.</param>
        private void DialogueViewCompletedDelivery(DialogueViewBase dialogueView)
        {
            // A dialogue view just completed its delivery. Remove it from
            // the set of active views.
            ActiveDialogueViews.Remove(dialogueView);

            // Have all of the views completed? 
            if (ActiveDialogueViews.Count == 0)
            {
                UpdateLineStatus(CurrentLine, LineStatus.Delivered);

                // Should the line automatically become Ended as soon as
                // it's Delivered?
                if (continueNextLineOnLineFinished)
                {
                    // Go ahead and notify the views. 
                    UpdateLineStatus(CurrentLine, LineStatus.Ended);

                    // Additionally, tell the views to dismiss the line
                    // from presentation. When each is done, it will notify
                    // this dialogue runner to call
                    // DialogueViewCompletedDismissal; when all have
                    // finished, this dialogue runner will tell the
                    // Dialogue to Continue() when all lines are done
                    // dismissing the line.
                    DismissLineFromViews(dialogueViews);
                }
            }
        }

        /// <summary>
        /// Updates a <see cref="LocalizedLine"/>'s status to <paramref
        /// name="newStatus"/>, and notifies all dialogue views that the
        /// line about the state change.
        /// </summary>
        /// <param name="line">The line whose state is changing.</param>
        /// <param name="newStatus">The <see
        /// cref="LineStatus"/> that the line now
        /// has.</param>
        private void UpdateLineStatus(LocalizedLine line, LineStatus newStatus)
        {
            var previousStatus = line.Status;

            Debug.Log($"Line \"{line.RawText}\" changed state to {newStatus}");

            // Update the state of the line and let the views know.
            line.Status = newStatus;

            foreach (var dialogueView in dialogueViews) {
                if (dialogueView == null) continue;

                dialogueView.OnLineStatusChanged(line);
            }
        }

        void ContinueDialogue()
        {
            wasCompleteCalled = true;
            CurrentLine = null;
            Dialogue.Continue();
        }

        /// <summary>
        /// Called by a <see cref="DialogueViewBase"/> derived class from
        /// <see cref="dialogueViews"/>
        /// to inform the <see cref="DialogueRunner"/> that the user
        /// intents to proceed to the next line.
        /// </summary>
        public void OnViewUserIntentNextLine() {
            
            if (CurrentLine == null) {
                // There's no active line, so there's nothing that can be
                // done here.
                Debug.LogWarning($"{nameof(OnViewUserIntentNextLine)} was called, but no line was running.");
                return;
            }

            switch (CurrentLine.Status)
            {
                case LineStatus.Running:
                    // The line has been Interrupted. Dialogue views should
                    // proceed to finish the delivery of the line as
                    // quickly as they can. (When all views have finished
                    // their expedited delivery, they call their completion
                    // handler as normal, and the line becomes Delivered.)
                    UpdateLineStatus(CurrentLine, LineStatus.Interrupted);
                    break;
                case LineStatus.Interrupted:
                    // The line was already interrupted, and the user has
                    // requested the next line again. We interpret this as
                    // the user being insistent. This means the line is now
                    // Ended, and the dialogue views must dismiss the line
                    // immediately.
                    UpdateLineStatus(CurrentLine, LineStatus.Ended);
                    break;
                case LineStatus.Delivered:
                    // The line had finished delivery (either normally or
                    // because it was Interrupted), and the user has
                    // indicated they want to proceed to the next line. The
                    // line is therefore Ended.
                    UpdateLineStatus(CurrentLine, LineStatus.Ended);
                    break;
                case LineStatus.Ended:
                    // The line has already been ended, so there's nothing
                    // further for the views to do. (This will only happen
                    // during the interval of time between a line becoming
                    // Ended and the next line appearing.)
                    break;                
            }    

            if (CurrentLine.Status == LineStatus.Ended) {
                // This line is Ended, so we need to tell the dialogue
                // views to dismiss it. 
                DismissLineFromViews(dialogueViews);
            }
            
        }

        private void DismissLineFromViews(IEnumerable<DialogueViewBase> dialogueViews)
        {
            ActiveDialogueViews.Clear();

            foreach (var dialogueView in dialogueViews) {
                if (dialogueView == null) continue;
                // we do this in two passes - first by adding each
                // dialogueView into ActiveDialogueViews, then by asking
                // them to dismiss the line - because calling
                // view.DismissLine might immediately call its completion
                // handler (which means that we'd be repeatedly returning
                // to zero active dialogue views, which means
                // DialogueViewCompletedDismissal will mark the line as
                // entirely done)
                ActiveDialogueViews.Add(dialogueView);
            }
                
            foreach (var dialogueView in dialogueViews) {
                if (dialogueView == null) continue;

                dialogueView.DismissLine(() => DialogueViewCompletedDismissal(dialogueView));
            }
        }

        private void DialogueViewCompletedDismissal(DialogueViewBase dialogueView)
        {
            // A dialogue view just completed dismissing its line. Remove
            // it from the set of active views.
            ActiveDialogueViews.Remove(dialogueView);

            // Have all of the views completed dismissal? 
            if (ActiveDialogueViews.Count == 0) {
                // Then we're ready to continue to the next piece of content.
                ContinueDialogue();
            }            
        }
        #endregion
    }

    #region Class/Interface

    /// <summary>
    /// An attribute that marks a method on a <see cref="MonoBehaviour"/>
    /// as a [command](<![CDATA[ {{<ref
    /// "/docs/unity/working-with-commands">}}]]>).
    /// </summary>
    /// <remarks>
    /// When a <see cref="DialogueRunner"/> receives a <see
    /// cref="Command"/>, and no command handler has been installed for the
    /// command, it splits it by spaces, and then checks to see if the
    /// second word, if any, is the name of any <see cref="GameObject"/>s
    /// in the scene. 
    ///
    /// If one is found, it is checked to see if any of the
    /// <see cref="MonoBehaviour"/>s attached to the class has a <see
    /// cref="YarnCommandAttribute"/> whose <see
    /// cref="YarnCommandAttribute.CommandString"/> matching the first word
    /// of the command.
    ///
    /// If a method is found, its parameters are checked:
    ///
    /// * If the method takes a single <see cref="string"/>[] parameter,
    /// the method is called, and will be passed an array containing all
    /// words in the command after the first two.
    ///
    /// * If the method takes a number of <see cref="string"/> parameters
    /// equal to the number of words in the command after the first two, it
    /// will be called with those words as parameters.
    ///
    /// * Otherwise, it will not be called, and a warning will be issued.
    ///
    /// ### `YarnCommand`s and Coroutines
    ///
    /// This attribute may be attached to a coroutine. 
    ///
    /// {{|note|}} The <see cref="DialogueRunner"/> determines if the
    /// method is a coroutine if the method returns <see
    /// cref="IEnumerator"/>. {{|/note|}}
    ///
    /// If the method is a coroutine, the DialogueRunner will pause
    /// execution until the coroutine ends.
    /// </remarks>
    /// <example>
    ///
    /// The following C# code uses the `YarnCommand` attribute to register
    /// commands.
    ///
    /// <![CDATA[
    /// ```csharp 
    /// class ExampleBehaviour : MonoBehaviour {
    ///         [YarnCommand("jump")] 
    ///         void Jump()
    ///         {
    ///             Debug.Log($"{this.gameObject.name} is jumping!");
    ///         }
    ///    
    ///         [YarnCommand("walk")] 
    ///         void WalkToDestination(string destination) {
    ///             Debug.Log($"{this.gameObject.name} is walking to {destination}!");
    ///         }
    ///     
    ///         [YarnCommand("shine_flashlight")] 
    ///         IEnumerator ShineFlashlight(string durationString) {
    ///             float.TryParse(durationString, out var duration);
    ///             Debug.Log($"{this.gameObject.name} is turning on the flashlight for {duration} seconds!");
    ///             yield new WaitForSeconds(duration);
    ///             Debug.Log($"{this.gameObject.name} is turning off the flashlight!");
    ///         }
    /// }
    /// ```
    /// ]]>
    ///
    /// Next, assume that this `ExampleBehaviour` script has been attached
    /// to a <see cref="GameObject"/> present in the scene named "Mae". The
    /// `Jump` and `WalkToDestination` methods may then be called from a
    /// Yarn script like so:
    ///
    /// <![CDATA[
    /// ```yarn 
    /// // Call the Jump() method in the ExampleBehaviour on Mae
    /// <<jump Mae>>
    ///
    /// // Call the WalkToDestination() method in the ExampleBehaviour 
    /// // on Mae, passing "targetPoint" as a parameter
    /// <<walk Mae targetPoint>>
    /// 
    /// // Call the ShineFlashlight method, passing "0.5" as a parameter;
    /// // dialogue will wait until the coroutine ends.
    /// <<shine_flashlight Mae 0.5>>
    /// ```
    /// ]]>
    ///
    /// Running this Yarn code will result in the following text being
    /// logged to the Console:
    ///
    /// ``` 
    /// Mae is jumping! 
    /// Mae is walking to targetPoint! 
    /// Mae is turning on the flashlight for 0.5 seconds!
    /// (... 0.5 seconds elapse ...)
    /// Mae is turning off the flashlight!
    /// ```
    /// </example>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class YarnCommandAttribute : System.Attribute
    {
        /// <summary>
        /// The name of the command, as it exists in Yarn.
        /// </summary>
        /// <remarks>
        /// This value does not have to be the same as the name of the
        /// method. For example, you could have a method named
        /// "`WalkToPoint`", and expose it to Yarn as a command named
        /// "`walk_to_point`".
        /// </remarks>        
        public string CommandString { get; set; }

        public YarnCommandAttribute(string commandString) => CommandString = commandString;
    }
    
    /// <summary>
    /// A <see cref="MonoBehaviour"/> that a <see cref="DialogueRunner"/>
    /// uses to store and retrieve variables.
    /// </summary>
    /// <remarks>
    /// This abstract class inherits from <see cref="MonoBehaviour"/>,
    /// which means that subclasses of this class can be attached to <see
    /// cref="GameObject"/>s.
    /// </remarks>
    public abstract class VariableStorageBehaviour : MonoBehaviour, Yarn.VariableStorage
    {
        /// <inheritdoc/>
        public abstract Value GetValue(string variableName);

        /// <inheritdoc/>
        public virtual void SetValue(string variableName, float floatValue) => SetValue(variableName, new Yarn.Value(floatValue));

        /// <inheritdoc/>
        public virtual void SetValue(string variableName, bool boolValue) => SetValue(variableName, new Yarn.Value(boolValue));

        /// <inheritdoc/>
        public virtual void SetValue(string variableName, string stringValue) => SetValue(variableName, new Yarn.Value(stringValue));

        /// <inheritdoc/>
        public abstract void SetValue(string variableName, Value value);

        /// <inheritdoc/>
        /// <remarks>
        /// The implementation in this abstract class throws a <see
        /// cref="NotImplementedException"/> when called. Subclasses of
        /// this class must provide their own implementation.
        /// </remarks>
        public virtual void Clear() => throw new NotImplementedException();

        /// <summary>
        /// Resets the VariableStorageBehaviour to its initial state.
        /// </summary>
        /// <remarks>
        /// This is similar to <see cref="Clear"/>, but additionally allows
        /// subclasses to restore any default values that should be
        /// present.
        /// </remarks>
        public abstract void ResetToDefaults();
    }

    /// <summary>
    /// The presentation status of a <see cref="LocalizedLine"/>.
    /// </summary>
    public enum LineStatus
    {
        /// <summary>
        /// The line is being build up and shown to the user.
        /// </summary>
        Running,
        /// <summary>
        /// The line got interrupted while being build up and should
        /// complete showing the line asap. View classes should get to the
        /// end of the line as fast as possible. A view class showing text
        /// would stop building up the text and immediately show the entire
        /// line and a view class playing voice over clips would do a very
        /// quick fade out and stop playback afterwards.
        /// </summary>
        Interrupted,
        /// <summary>
        /// The line has been fully presented to the user. A view class
        /// presenting the line as text would be showing the entire line
        /// and a view class playing voice over clips would be silent now.
        /// </summary>
        /// <remarks>
        /// A line that was previously <see cref="LineStatus.Interrupted"/>
        /// will become <see cref="LineStatus.Delivered"/> once the <see
        /// cref="DialogueViewBase"/> has completed the interruption
        /// process.
        /// </remarks>
        Delivered,
        /// <summary>
        /// The line is not being presented anymore in any way to the user.
        /// </summary>
        Ended
    }

    

    public class DialogueOption {
        /// <summary>
        /// The ID of this dialogue option
        /// </summary>
        public int DialogueOptionID;
        /// <summary>
        /// The ID of the dialogue option's text
        /// </summary>
        public string TextID;
        /// <summary>
        /// The line for this dialogue option
        /// </summary>
        public LocalizedLine Line;
    }

    #endregion
}
