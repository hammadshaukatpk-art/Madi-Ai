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
   FileWriteString(file, key + "=" + CleanValue(value) + "\r\n");
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

int OnInit()
{
   EventSetTimer(2);
   PublishStatus();
   Print("Madi AI read-only local bridge started.");
   return INIT_SUCCEEDED;
}

void OnTimer()
{
   PublishStatus();
}

void OnDeinit(const int reason)
{
   EventKillTimer();
   Print("Madi AI bridge stopped. Saved status will expire automatically.");
}
