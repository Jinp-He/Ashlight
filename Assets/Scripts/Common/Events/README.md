# GameEvent 事件系统使用指南

## 概述

GameEvent是一个基于订阅/发布模式的事件总线系统，用于实现系统间的解耦通信。

## 核心概念

### 事件总线（Event Bus）
- **解耦通信**：发送者和接收者不需要直接引用
- **类型安全**：基于泛型的强类型事件
- **简单易用**：只需定义事件类，无需继承接口

### 订阅/发布模式
```
发布者 → GameEvent.Publish() → 事件总线 → 所有订阅者
```

## 基本用法

### 1. 定义事件类

首先定义事件数据结构：

```csharp
using Ashlight.Common.Events;

// 卡牌选择事件
public class CardSelectedEvent
{
    public string CardId;
    public CardInfo CardInfo;
    
    public CardSelectedEvent(string cardId, CardInfo cardInfo)
    {
        CardId = cardId;
        CardInfo = cardInfo;
    }
}

// 配置加载完成事件
public class ConfigLoadedEvent
{
    public bool Success;
    public string Message;
}

// 简单的无参数事件
public class GameStartEvent { }
```

### 2. 订阅事件

在需要接收事件的地方订阅：

```csharp
using Ashlight.Common.Events;

public class DeckManager : MonoBehaviour
{
    private void OnEnable()
    {
        // 订阅卡牌选择事件
        GameEvent.Subscribe<CardSelectedEvent>(OnCardSelected);
        
        // 订阅配置加载事件
        GameEvent.Subscribe<ConfigLoadedEvent>(OnConfigLoaded);
    }
    
    private void OnDisable()
    {
        // 取消订阅（重要！避免内存泄漏）
        GameEvent.Unsubscribe<CardSelectedEvent>(OnCardSelected);
        GameEvent.Unsubscribe<ConfigLoadedEvent>(OnConfigLoaded);
    }
    
    // 事件处理方法
    private void OnCardSelected(CardSelectedEvent evt)
    {
        Debug.Log($"收到卡牌选择事件: {evt.CardId}");
        // 处理卡牌选择逻辑
    }
    
    private void OnConfigLoaded(ConfigLoadedEvent evt)
    {
        if (evt.Success)
        {
            Debug.Log("配置加载成功");
        }
    }
}
```

### 3. 发布事件

在需要发送事件的地方发布：

```csharp
using Ashlight.Common.Events;

public class CardViewController : MonoBehaviour
{
    public void OnCardClicked()
    {
        // 发布卡牌选择事件
        var evt = new CardSelectedEvent(_currentCard.Id, _currentCard);
        GameEvent.Publish(evt);
    }
}

public class ConfigLoader
{
    public static void Load()
    {
        try
        {
            // 加载配置...
            
            // 发布成功事件
            GameEvent.Publish(new ConfigLoadedEvent 
            { 
                Success = true, 
                Message = "配置加载成功" 
            });
        }
        catch (Exception e)
        {
            // 发布失败事件
            GameEvent.Publish(new ConfigLoadedEvent 
            { 
                Success = false, 
                Message = e.Message 
            });
        }
    }
}
```

## 实际应用示例

### 示例1：卡牌系统事件

```csharp
// ========== 定义事件 ==========
namespace Ashlight.Events
{
    // 卡牌被选中
    public class CardSelectedEvent
    {
        public CardInfo Card;
        public DescriptionMode DisplayMode;
    }
    
    // 卡牌被使用
    public class CardUsedEvent
    {
        public string CardId;
        public int TargetIndex;
    }
    
    // 卡组更新
    public class DeckUpdatedEvent
    {
        public List<CardInfo> Cards;
    }
}

// ========== 发布者：CardViewController ==========
public partial class CardViewController
{
    public void OnCardClicked()
    {
        // 发布卡牌选择事件
        GameEvent.Publish(new CardSelectedEvent
        {
            Card = _currentCard,
            DisplayMode = _displayMode
        });
    }
    
    public void UseCard(int targetIndex)
    {
        // 发布卡牌使用事件
        GameEvent.Publish(new CardUsedEvent
        {
            CardId = _currentCard.Id,
            TargetIndex = targetIndex
        });
    }
}

// ========== 订阅者：DeckManager ==========
public class DeckManager : MonoBehaviour
{
    private void OnEnable()
    {
        GameEvent.Subscribe<CardSelectedEvent>(OnCardSelected);
        GameEvent.Subscribe<CardUsedEvent>(OnCardUsed);
    }
    
    private void OnDisable()
    {
        GameEvent.Unsubscribe<CardSelectedEvent>(OnCardSelected);
        GameEvent.Unsubscribe<CardUsedEvent>(OnCardUsed);
    }
    
    private void OnCardSelected(CardSelectedEvent evt)
    {
        Debug.Log($"卡牌被选中: {evt.Card.Name}");
        // 更新UI显示
    }
    
    private void OnCardUsed(CardUsedEvent evt)
    {
        Debug.Log($"卡牌被使用: {evt.CardId}");
        // 从手牌移除
        // 触发效果
    }
}

// ========== 订阅者：BattleSystem ==========
public class BattleSystem : MonoBehaviour
{
    private void OnEnable()
    {
        GameEvent.Subscribe<CardUsedEvent>(OnCardUsed);
    }
    
    private void OnDisable()
    {
        GameEvent.Unsubscribe<CardUsedEvent>(OnCardUsed);
    }
    
    private void OnCardUsed(CardUsedEvent evt)
    {
        // 执行战斗逻辑
        // 应用卡牌效果
        // 更新战斗状态
    }
}
```

### 示例2：UI系统事件

```csharp
// ========== 定义事件 ==========
public class UIOpenEvent
{
    public string PanelName;
    public object Data;
}

public class UICloseEvent
{
    public string PanelName;
}

// ========== 发布者 ==========
public class UIManager
{
    public void OpenPanel(string panelName, object data = null)
    {
        // 打开面板...
        
        // 发布事件
        GameEvent.Publish(new UIOpenEvent
        {
            PanelName = panelName,
            Data = data
        });
    }
    
    public void ClosePanel(string panelName)
    {
        // 关闭面板...
        
        // 发布事件
        GameEvent.Publish(new UICloseEvent { PanelName = panelName });
    }
}

// ========== 订阅者 ==========
public class AudioManager : MonoBehaviour
{
    private void OnEnable()
    {
        GameEvent.Subscribe<UIOpenEvent>(OnUIOpen);
        GameEvent.Subscribe<UICloseEvent>(OnUIClose);
    }
    
    private void OnDisable()
    {
        GameEvent.Unsubscribe<UIOpenEvent>(OnUIOpen);
        GameEvent.Unsubscribe<UICloseEvent>(OnUIClose);
    }
    
    private void OnUIOpen(UIOpenEvent evt)
    {
        PlaySound($"ui_open_{evt.PanelName}");
    }
    
    private void OnUIClose(UICloseEvent evt)
    {
        PlaySound("ui_close");
    }
}
```

### 示例3：游戏状态事件

```csharp
// ========== 定义事件 ==========
public class GameStateChangedEvent
{
    public GameState OldState;
    public GameState NewState;
}

public enum GameState
{
    MainMenu,
    DeckBuilding,
    Battle,
    GameOver
}

// ========== 发布者：GameManager ==========
public class GameManager : MonoBehaviour
{
    private GameState _currentState;
    
    public void ChangeState(GameState newState)
    {
        var oldState = _currentState;
        _currentState = newState;
        
        // 发布状态改变事件
        GameEvent.Publish(new GameStateChangedEvent
        {
            OldState = oldState,
            NewState = newState
        });
    }
}

// ========== 订阅者：各个系统 ==========
public class UIController : MonoBehaviour
{
    private void OnEnable()
    {
        GameEvent.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
    }
    
    private void OnDisable()
    {
        GameEvent.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
    }
    
    private void OnGameStateChanged(GameStateChangedEvent evt)
    {
        // 根据游戏状态切换UI
        switch (evt.NewState)
        {
            case GameState.MainMenu:
                ShowMainMenu();
                break;
            case GameState.DeckBuilding:
                ShowDeckBuilder();
                break;
            case GameState.Battle:
                ShowBattleUI();
                break;
        }
    }
}
```

## 最佳实践

### 1. 事件命名规范

```csharp
// ✅ 好的命名
public class CardSelectedEvent { }       // 清晰的动作
public class PlayerHealthChangedEvent { } // 完整的描述
public class ConfigLoadedEvent { }        // 过去式表示完成

// ❌ 不好的命名
public class CardEvent { }     // 太笼统
public class Event1 { }        // 无意义
public class Data { }          // 不是事件
```

### 2. 订阅/取消订阅时机

```csharp
public class MyComponent : MonoBehaviour
{
    // ✅ 推荐：在OnEnable/OnDisable中
    private void OnEnable()
    {
        GameEvent.Subscribe<MyEvent>(OnMyEvent);
    }
    
    private void OnDisable()
    {
        GameEvent.Unsubscribe<MyEvent>(OnMyEvent);
    }
    
    // ✅ 或在Start/OnDestroy中
    private void Start()
    {
        GameEvent.Subscribe<MyEvent>(OnMyEvent);
    }
    
    private void OnDestroy()
    {
        GameEvent.Unsubscribe<MyEvent>(OnMyEvent);
    }
}
```

### 3. 避免内存泄漏

**⚠️ 重要：必须成对调用订阅和取消订阅**

```csharp
// ❌ 错误：只订阅不取消，导致内存泄漏
public class BadExample : MonoBehaviour
{
    private void Start()
    {
        GameEvent.Subscribe<MyEvent>(OnMyEvent);
        // 忘记在OnDestroy中取消订阅！
    }
}

// ✅ 正确：成对调用
public class GoodExample : MonoBehaviour
{
    private void Start()
    {
        GameEvent.Subscribe<MyEvent>(OnMyEvent);
    }
    
    private void OnDestroy()
    {
        GameEvent.Unsubscribe<MyEvent>(OnMyEvent);
    }
}
```

### 4. 事件数据设计

```csharp
// ✅ 好的事件设计：包含完整信息
public class CardDrawnEvent
{
    public CardInfo Card;
    public int PlayerIndex;
    public int HandCount;
    public bool IsFromDeck;
}

// ❌ 不好的设计：信息不足
public class CardEvent
{
    public string Id;  // 只有ID，订阅者还需要查询完整信息
}

// ✅ 使用属性简化创建
public class PlayerDamagedEvent
{
    public int PlayerId { get; set; }
    public int Damage { get; set; }
    public int RemainingHealth { get; set; }
    public string Source { get; set; }
}
```

### 5. 事件的发送时机

```csharp
public class CardManager
{
    public void AddCard(CardInfo card)
    {
        // 1. 先执行业务逻辑
        _cards.Add(card);
        UpdateUI();
        
        // 2. 最后发布事件（让其他系统响应）
        GameEvent.Publish(new CardAddedEvent { Card = card });
    }
}
```

## 高级用法

### 1. 链式事件

```csharp
// 事件A触发事件B
public class SystemA : MonoBehaviour
{
    private void OnEnable()
    {
        GameEvent.Subscribe<EventA>(OnEventA);
    }
    
    private void OnEventA(EventA evt)
    {
        // 处理事件A
        ProcessEventA(evt);
        
        // 触发事件B
        GameEvent.Publish(new EventB { Data = evt.Result });
    }
}
```

### 2. 条件订阅

```csharp
public class ConditionalSubscriber : MonoBehaviour
{
    private bool _isSubscribed = false;
    
    public void EnableFeature()
    {
        if (!_isSubscribed)
        {
            GameEvent.Subscribe<MyEvent>(OnMyEvent);
            _isSubscribed = true;
        }
    }
    
    public void DisableFeature()
    {
        if (_isSubscribed)
        {
            GameEvent.Unsubscribe<MyEvent>(OnMyEvent);
            _isSubscribed = false;
        }
    }
}
```

### 3. 调试和日志

```csharp
public class EventLogger : MonoBehaviour
{
    private void OnEnable()
    {
        // 订阅所有感兴趣的事件
        GameEvent.Subscribe<CardSelectedEvent>(LogEvent);
        GameEvent.Subscribe<CardUsedEvent>(LogEvent);
        GameEvent.Subscribe<GameStateChangedEvent>(LogEvent);
    }
    
    private void LogEvent<T>(T evt)
    {
        Debug.Log($"[Event] {typeof(T).Name}: {JsonUtility.ToJson(evt)}");
    }
}
```

## 常见问题

### Q1: 事件订阅顺序重要吗？
**A**: 是的。事件会按订阅顺序依次调用。如果有顺序依赖，需要注意订阅时机。

### Q2: 可以在事件处理中发布新事件吗？
**A**: 可以，但要避免循环事件导致的死循环。

### Q3: 事件是同步还是异步？
**A**: 当前是同步的，`Publish`会立即调用所有订阅者。

### Q4: 如何清除所有事件订阅？
**A**: 使用`GameEvent.Clear()`，通常在场景切换时调用。

```csharp
private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
{
    // 清除所有事件订阅，避免跨场景引用
    GameEvent.Clear();
}
```

## 性能考虑

1. **订阅数量**：合理的订阅数量不会有性能问题
2. **事件频率**：避免在Update中频繁发布事件
3. **事件数据**：避免在事件中传递大对象，考虑使用引用

## 与其他系统的对比

| 特性 | GameEvent | UnityEvent | C# Event |
|------|-----------|------------|----------|
| 类型安全 | ✅ | ✅ | ✅ |
| 解耦程度 | 高 | 中 | 低 |
| Inspector可见 | ❌ | ✅ | ❌ |
| 动态订阅 | ✅ | ❌ | ✅ |
| 跨系统通信 | ✅ | ❌ | ❌ |

## 总结

GameEvent适用于：
- ✅ 系统间解耦通信
- ✅ 跨模块事件通知
- ✅ 全局状态变化广播
- ✅ 动态订阅/取消订阅

不适用于：
- ❌ UI按钮点击（使用UnityEvent）
- ❌ 紧密耦合的组件间通信（直接调用）
- ❌ 高频率更新（如Update中使用）

