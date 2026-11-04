//+------------------------------------------------------------------+
//| MadiAI_Bridge.mq5 - free, read-only MT5 account status bridge     |
//| Install into MQL5/Experts, compile in MetaEditor, attach to chart|
//| No DLL imports, no network, no trade execution or account edits. |
//+------------------------------------------------------------------+
#property strict
#property version "1.00"
#property description "Read-only local account and open-position status for Madi AI"

string CleanValue(string input)
{
   StringReplace(input, "\r", " ");
   StringReplace(input, "\n", " ");
   StringReplace(input, "|", " ");
   StringReplace(input, "=", " ");
   if(StringLen(input) > 120)
      input = StringSubstr(input, 0, 120);
   return input;
}

void WriteLine(const int file, const string key, const string value)
{
   // Preserve the field separator for structured position and candle records.
   // Free-text account fields still have control characters removed.
   string safe = (key == "position" || key == "candle") ? value : CleanValue(value);
   FileWriteString(file, key + "=" + safe + "\r\n");
}

void PublishStatus()
{
   FolderCreate("MadiAI", FILE_COMMON);
   int file = FileOpen("MadiAI\\status.txt",
                       FILE_WRITE | FILE_TXT | FILE_ANSI | FILE_COMMON);
   if(file == INVALID_HANDLE)
   {
      Print("Madi AI: cannot write common status file, error ", GetLastError());
      return;
   }

   WriteLine(file, "updated", TimeToString(TimeLocal(), TIME_DATE | TIME_SECONDS));
   WriteLine(file, "broker", AccountInfoString(ACCOUNT_SERVER));
   WriteLine(file, "login", IntegerToString(AccountInfoInteger(ACCOUNT_LOGIN)));
   WriteLine(file, "currency", AccountInfoString(ACCOUNT_CURRENCY));
   WriteLine(file, "balance", DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE), 2));
   WriteLine(file, "equity", DoubleToString(AccountInfoDouble(ACCOUNT_EQUITY), 2));
   WriteLine(file, "free_margin", DoubleToString(AccountInfoDouble(ACCOUNT_MARGIN_FREE), 2));
   WriteLine(file, "chart_symbol", _Symbol);
   WriteLine(file, "chart_timeframe", EnumToString(_Period));
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   WriteLine(file, "bid", DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_BID), digits));
   WriteLine(file, "ask", DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_ASK), digits));

   // Export only completed candles. The active forming candle is intentionally
   // excluded so that chart statistics do not shift on every incoming tick.
   MqlRates rates[];
   int bars = CopyRates(_Symbol, _Period, 1, 80, rates);
   if(bars > 0)
   {
      for(int b = 0; b < bars; b++)
      {
         string candle = TimeToString(rates[b].time, TIME_DATE | TIME_SECONDS) + "|" +
                         DoubleToString(rates[b].open, digits) + "|" +
                         DoubleToString(rates[b].high, digits) + "|" +
                         DoubleToString(rates[b].low, digits) + "|" +
                         DoubleToString(rates[b].close, digits) + "|" +
                         IntegerToString(rates[b].tick_volume);
         WriteLine(file, "candle", candle);
      }
   }

   int total = PositionsTotal();
   int limit = MathMin(total, 40);
   for(int i = 0; i < limit; i++)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      string symbol = PositionGetString(POSITION_SYMBOL);
      string side = PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY ? "BUY" : "SELL";
      string row = IntegerToString((long)ticket) + "|" + CleanValue(symbol) + "|" +
                   side + "|" + DoubleToString(PositionGetDouble(POSITION_VOLUME), 2) +
                   "|" + DoubleToString(PositionGetDouble(POSITION_PROFIT), 2);
      WriteLine(file, "position", row);
   }
   FileFlush(file);
   FileClose(file);
}

ENUM_TIMEFRAMES ParseTimeframe(string value)
{
   if(value == "M1") return PERIOD_M1;
   if(value == "M5") return PERIOD_M5;
   if(value == "M15") return PERIOD_M15;
   if(value == "M30") return PERIOD_M30;
   if(value == "H1") return PERIOD_H1;
   if(value == "H4") return PERIOD_H4;
   if(value == "D1") return PERIOD_D1;
   if(value == "W1") return PERIOD_W1;
   if(value == "MN1") return PERIOD_MN1;
   return PERIOD_CURRENT;
}

void ProcessChartRequest()
{
   string path = "MadiAI\\command.txt";
   if(!FileIsExist(path, FILE_COMMON)) return;
   int handle = FileOpen(path, FILE_READ | FILE_TXT | FILE_ANSI | FILE_COMMON);
   if(handle == INVALID_HANDLE) return;
   string command = FileReadString(handle);
   FileClose(handle);
   FileDelete(path, FILE_COMMON);

   string parts[];
   if(StringSplit(command, '|', parts) != 3) return;
   if(parts[0] != "chart") return;
   string symbol = parts[1];
   string frame = parts[2];
   StringTrimLeft(symbol);
   StringTrimRight(symbol);
   StringTrimLeft(frame);
   StringTrimRight(frame);
   if(StringLen(symbol) < 2 || StringLen(symbol) > 20) return;
   if(StringLen(frame) < 2 || StringLen(frame) > 3) return;
   for(int i = 0; i < StringLen(symbol); i++)
   {
      ushort c = StringGetCharacter(symbol, i);
      if(!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
           c == '.' || c == '_' || c == '-')) return;
   }
   ENUM_TIMEFRAMES period = ParseTimeframe(frame);
   if(period == PERIOD_CURRENT) return;
   if(!SymbolSelect(symbol, true))
   {
      Print("Madi AI: symbol unavailable ", symbol);
      return;
   }
   if(!ChartSetSymbolPeriod(0, symbol, period))
      Print("Madi AI: could not switch chart ", symbol, " ", frame);
}

int OnInit()
{
   EventSetTimer(2);
   PublishStatus();
   Print("Madi AI read-only local bridge started.");
   return INIT_SUCCEEDED;
}

void OnTimer()
{
   ProcessChartRequest();
   PublishStatus();
}

void OnDeinit(const int reason)
{
   EventKillTimer();
   Print("Madi AI bridge stopped. Saved status will expire automatically.");
}
