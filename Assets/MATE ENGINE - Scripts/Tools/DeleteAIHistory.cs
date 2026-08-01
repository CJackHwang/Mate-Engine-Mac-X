using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class DeleteAIHistory : MonoBehaviour
{
    [Header("UI Button to delete AI history")]
    public Button deleteButton;

    void Start()
    {
        if (deleteButton != null)
            deleteButton.onClick.AddListener(DeleteHistoryFiles);
    }

    public void DeleteHistoryFiles()
    {
        // Clear AnthropicChatHandler history in memory
        var handler = FindAnyObjectByType<AnthropicChatHandler>();
        if (handler != null) handler.ClearHistory();

        // Delete legacy LLMUnity files if they exist
        string[] legacyFiles = { "ZomeAI.json", "ZomeAI.cache" };
        bool deletedSomething = false;
        foreach (var f in legacyFiles)
        {
            string path = Path.Combine(Application.persistentDataPath, f);
            if (File.Exists(path)) { File.Delete(path); deletedSomething = true; Debug.Log("[DeleteAIHistory] Deleted: " + path); }
        }

        if (!deletedSomething)
            Debug.Log("[DeleteAIHistory] History cleared (in-memory).");
    }
}
