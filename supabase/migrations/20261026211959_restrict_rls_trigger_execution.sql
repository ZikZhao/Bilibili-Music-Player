-- The dashboard's automatic-RLS event trigger runs as its owner. Clients do not
-- need to invoke the trigger function through the Data API.
do $$ begin
    if to_regprocedure('public.rls_auto_enable()') is not null then
        revoke execute on function public.rls_auto_enable() from public, anon, authenticated;
    end if;
end $$;
