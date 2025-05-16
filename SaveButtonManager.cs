using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SaveButtonManager : MonoBehaviour
{
    [Header("Button References")]
    public Button saveButton;
    public Button saveAndExitButton;
    public Button exitWithoutSavingButton;

    [Header("Dialog References")]
    public GameObject saveDialogPanel;
    public TMP_InputField filenameInputField;
    public Button confirmSaveButton;
    public Button cancelSaveButton;
    public TextMeshProUGUI saveStatusText;

    [Header("Exit Dialog References")]
    public GameObject exitConfirmationPanel;
    public Button confirmExitButton;
    public Button cancelExitButton;

    [Header("Manager References")]
    public HitPointManager hitPointManager;
    public NavigationManager navigationManager;
    public AccessibilityManager accessibilityManager;

    // State tracking
    private bool isSaving = false;
    private bool isExiting = false;
    private string defaultFilename;

    void Start()
    {
        // Find references if not set
        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (navigationManager == null)
            navigationManager = FindObjectOfType<NavigationManager>();

        if (accessibilityManager == null)
            accessibilityManager = FindObjectOfType<AccessibilityManager>();

        // Set up default filename with timestamp
        defaultFilename = "Path_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // Hide dialogs initially
        if (saveDialogPanel != null)
            saveDialogPanel.SetActive(false);

        if (exitConfirmationPanel != null)
            exitConfirmationPanel.SetActive(false);

        // Initialize button listeners
        SetupButtonListeners();
    }

    private void SetupButtonListeners()
    {
        // Save button
        if (saveButton != null)
        {
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(ShowSaveDialog);
        }

        // Save and Exit button
        if (saveAndExitButton != null)
        {
            saveAndExitButton.onClick.RemoveAllListeners();
            saveAndExitButton.onClick.AddListener(SaveAndExit);
        }

        // Exit without saving button
        if (exitWithoutSavingButton != null)
        {
            exitWithoutSavingButton.onClick.RemoveAllListeners();
            exitWithoutSavingButton.onClick.AddListener(ShowExitConfirmation);
        }

        // Save dialog buttons
        if (confirmSaveButton != null)
        {
            confirmSaveButton.onClick.RemoveAllListeners();
            confirmSaveButton.onClick.AddListener(ConfirmSave);
        }

        if (cancelSaveButton != null)
        {
            cancelSaveButton.onClick.RemoveAllListeners();
            cancelSaveButton.onClick.AddListener(CancelSave);
        }

        // Exit confirmation buttons
        if (confirmExitButton != null)
        {
            confirmExitButton.onClick.RemoveAllListeners();
            confirmExitButton.onClick.AddListener(ConfirmExit);
        }

        if (cancelExitButton != null)
        {
            cancelExitButton.onClick.RemoveAllListeners();
            cancelExitButton.onClick.AddListener(CancelExit);
        }
    }

    // Show save dialog
    public void ShowSaveDialog()
    {
        if (isSaving)
            return;

        isSaving = true;

        // Make sure we have points to save
        if (hitPointManager == null || hitPointManager.poseClassList.Count == 0)
        {
            // Nothing to save
            if (accessibilityManager != null && accessibilityManager.textToSpeech != null)
            {
                accessibilityManager.textToSpeech.Speak("No path data to save.");
            }

            if (saveStatusText != null)
            {
                saveStatusText.text = "No path data to save.";
            }

            isSaving = false;
            return;
        }

        // Set default filename
        if (filenameInputField != null)
        {
            filenameInputField.text = defaultFilename;
        }

        // Show save dialog
        if (saveDialogPanel != null)
        {
            saveDialogPanel.SetActive(true);

            // Focus input field
            if (filenameInputField != null)
            {
                filenameInputField.Select();
                filenameInputField.ActivateInputField();
            }

            // Announce for accessibility
            if (accessibilityManager != null && accessibilityManager.textToSpeech != null)
            {
                accessibilityManager.textToSpeech.Speak("Enter a name to save the path.");
            }
        }
    }

    // Save and exit
    public void SaveAndExit()
    {
        if (isExiting)
            return;

        isExiting = true;

        // Check if we have data to save
        if (hitPointManager == null || hitPointManager.poseClassList.Count == 0)
        {
            // Nothing to save, just exit
            ExitApplication();
            return;
        }

        // Show save dialog with exit flag
        ShowSaveDialog();

        // Set exit flag to true
        isExiting = true;
    }

    // Confirm save button handler
    public void ConfirmSave()
    {
        if (hitPointManager == null)
            return;

        string filename = filenameInputField != null ? filenameInputField.text : defaultFilename;

        // Make sure we have a filename
        if (string.IsNullOrEmpty(filename))
        {
            filename = defaultFilename;
        }

        // Save the path data
        hitPointManager.SaveAllTheInformationToFile(filename);

        // Hide dialog
        if (saveDialogPanel != null)
        {
            saveDialogPanel.SetActive(false);
        }

        // Provide feedback
        if (accessibilityManager != null && accessibilityManager.textToSpeech != null)
        {
            accessibilityManager.textToSpeech.Speak("Path saved successfully as " + filename);
        }

        // Update status text
        if (saveStatusText != null)
        {
            saveStatusText.text = "Path saved as: " + filename;
        }

        // Reset state
        isSaving = false;

        // Exit if this was a save and exit action
        if (isExiting)
        {
            StartCoroutine(DelayedExit(1.5f));
        }
    }

    // Cancel save button handler
    public void CancelSave()
    {
        // Hide dialog
        if (saveDialogPanel != null)
        {
            saveDialogPanel.SetActive(false);
        }

        // Provide feedback
        if (accessibilityManager != null && accessibilityManager.textToSpeech != null)
        {
            accessibilityManager.textToSpeech.Speak("Save cancelled.");
        }

        // Reset state
        isSaving = false;
        isExiting = false;
    }

    // Show exit confirmation dialog
    public void ShowExitConfirmation()
    {
        if (exitConfirmationPanel != null)
        {
            exitConfirmationPanel.SetActive(true);

            // Announce for accessibility
            if (accessibilityManager != null && accessibilityManager.textToSpeech != null)
            {
                if (hitPointManager != null && hitPointManager.poseClassList.Count > 0)
                {
                    accessibilityManager.textToSpeech.Speak("You have unsaved path data. Exit without saving?");
                }
                else
                {
                    accessibilityManager.textToSpeech.Speak("Exit application?");
                }
            }
        }
    }

    // Confirm exit button handler
    public void ConfirmExit()
    {
        // Hide dialog
        if (exitConfirmationPanel != null)
        {
            exitConfirmationPanel.SetActive(false);
        }

        // Exit application
        ExitApplication();
    }

    // Cancel exit button handler
    public void CancelExit()
    {
        // Hide dialog
        if (exitConfirmationPanel != null)
        {
            exitConfirmationPanel.SetActive(false);
        }

        // Provide feedback
        if (accessibilityManager != null && accessibilityManager.textToSpeech != null)
        {
            accessibilityManager.textToSpeech.Speak("Exit cancelled.");
        }

        // Reset state
        isExiting = false;
    }

    // Exit application
    private void ExitApplication()
    {
        // Provide feedback
        if (accessibilityManager != null && accessibilityManager.textToSpeech != null)
        {
            accessibilityManager.textToSpeech.Speak("Exiting application. Goodbye.");
        }

        // Stop navigation if active
        if (navigationManager != null && navigationManager.isNavigating)
        {
            navigationManager.StopNavigation();
        }

        Debug.Log("Exiting application");

        // Actual exit (after short delay for TTS to finish)
        StartCoroutine(DelayedExit(1.5f));
    }

    private IEnumerator DelayedExit(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // Exit based on platform
#if UNITY_EDITOR
        // In editor, stop play mode
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // On device, quit application
        Application.Quit();
#endif
    }
}